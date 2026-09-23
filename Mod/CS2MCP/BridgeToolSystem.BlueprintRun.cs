using System;
using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2MCP
{
    public sealed partial class BridgeToolSystem
    {
        internal sealed class BlueprintPrefabEntry
        {
            public Entity Entity;
            public PrefabBase Base;
        }

        private BlueprintRecord m_BlueprintRecord;
        private BlueprintRunRecord m_BlueprintRun;
        private Dictionary<string, BlueprintPrefabEntry> m_BlueprintPrefabs;
        private string m_BlueprintCourseId;
        private DateTime m_BlueprintHeartbeatUtc;

        private const float kBlueprintAnchorDriftMax = 2f;

        /// <summary>Must be called on the simulation thread.</summary>
        internal bool TryQueueBlueprint(
            BlueprintRecord record,
            BlueprintRunRecord run,
            Dictionary<string, BlueprintPrefabEntry> prefabs,
            BridgeRequest request)
        {
            if (m_Stage != Stage.Idle)
            {
                return false;
            }
            m_PendingKind = OperationKind.Blueprint;
            m_BlueprintRecord = record;
            m_BlueprintRun = run;
            m_BlueprintPrefabs = prefabs;
            m_PendingRequest = request;
            m_BlueprintHeartbeatUtc = DateTime.UtcNow;
            ClearAutoConnect();
            Activate();
            if (!QueueNextBlueprintStep())
            {
                m_Stage = Stage.Finish;
            }
            else
            {
                m_Stage = Stage.CreateDefinitions;
            }
            return true;
        }

        internal bool BlueprintRunActive(out double heartbeatAgeSeconds)
        {
            heartbeatAgeSeconds = double.MaxValue;
            if (m_Stage == Stage.Idle || m_PendingKind != OperationKind.Blueprint || m_BlueprintRun == null)
            {
                return false;
            }
            heartbeatAgeSeconds = (DateTime.UtcNow - m_BlueprintHeartbeatUtc).TotalSeconds;
            return true;
        }

        private bool QueueNextBlueprintStep()
        {
            string next = NetworkBlueprintStore.NextReadyStep(m_BlueprintRecord, m_BlueprintRun);
            if (next == null)
            {
                NetworkBlueprintStore.MarkStepBlocked(m_BlueprintRun, FirstPendingStep(),
                    "its build dependency is unfinished; inspect the earlier blocking step");
                CompletePending(BlueprintRunResponse("blocked",
                    "no buildable step remains while work is unfinished; inspect the run for the blocking step"));
                return false;
            }
            ResolvedCourse planned = null;
            foreach (ResolvedCourse course in m_BlueprintRecord.Courses)
            {
                if (course.Id == next)
                {
                    planned = course;
                    break;
                }
            }
            if (planned == null)
            {
                CompletePending(BlueprintRunResponse("blocked", "planned step '" + next + "' is missing from the revision"));
                return false;
            }
            BlueprintPrefabEntry prefab;
            if (m_BlueprintPrefabs == null || !m_BlueprintPrefabs.TryGetValue(planned.Prefab, out prefab))
            {
                CompletePending(BlueprintRunResponse("blocked", "road prefab '" + planned.Prefab + "' is unavailable; revise the sketch"));
                NetworkBlueprintStore.MarkStepBlocked(m_BlueprintRun, planned.Id, "prefab unavailable");
                return false;
            }
            CompiledRoadCourse stepCourse;
            string courseError;
            if (!TryBuildStepCourse(planned, out stepCourse, out courseError))
            {
                CompletePending(BlueprintRunResponse("blocked", courseError));
                NetworkBlueprintStore.MarkStepBlocked(m_BlueprintRun, planned.Id, courseError);
                return false;
            }
            m_CompiledRoadCourse = stepCourse;
            m_PendingPrefabEntity = prefab.Entity;
            m_PendingPrefab = prefab.Base;
            m_PendingPosition = stepCourse.Path.A;
            m_PendingEnd = stepCourse.Path.D;
            m_PendingMid = default;
            m_PendingHasMid = false;
            m_PendingElevations = stepCourse.Elevations;
            m_PendingRoadMode = stepCourse.Mode;
            m_PendingRoadPath = stepCourse.Path;
            m_PendingRotation = quaternion.identity;
            m_BlueprintCourseId = planned.Id;
            m_Stage = Stage.CreateDefinitions;
            return true;
        }

        private string FirstPendingStep()
        {
            foreach (BlueprintStepRecord step in m_BlueprintRun.Steps)
            {
                if (step.State == BlueprintStepState.Pending)
                {
                    return step.CourseId;
                }
            }
            return null;
        }

        private bool TryBuildStepCourse(ResolvedCourse planned, out CompiledRoadCourse course, out string error)
        {
            course = null;
            error = null;
            RoadConnection start;
            RoadConnection end;
            float3 startDelta = default;
            float3 endDelta = default;
            if (!ResolveBlueprintAnchor(planned.Start, planned.Path.A, out start, out startDelta, out error)
                || !ResolveBlueprintAnchor(planned.End, planned.Path.D, out end, out endDelta, out error))
            {
                return false;
            }
            RoadPath path = new RoadPath(
                planned.Path.A + startDelta,
                planned.Path.B + startDelta,
                planned.Path.C + endDelta,
                planned.Path.D + endDelta);
            TerrainHeightData heights = World.GetOrCreateSystemManaged<TerrainSystem>().GetHeightData();
            var elevations = new float2(
                path.A.y - TerrainUtils.SampleHeight(ref heights, path.A),
                path.D.y - TerrainUtils.SampleHeight(ref heights, path.D));
            if (!CompiledRoadCourse.TryCreate(path, planned.Mode, elevations, start, end, out course, out error))
            {
                error = "planned step '" + planned.Id + "' no longer fits native limits: " + error;
                return false;
            }
            return true;
        }

        private bool ResolveBlueprintAnchor(
            BlueprintAnchorRef anchor, float3 planned, out RoadConnection connection, out float3 delta, out string error)
        {
            connection = default;
            delta = default;
            error = null;
            if (anchor.Kind == BlueprintAnchorKind.None)
            {
                return true;
            }
            Entity entity;
            float split;
            if (anchor.Kind == BlueprintAnchorKind.Existing)
            {
                entity = new Entity { Index = anchor.EntityIndex, Version = anchor.EntityVersion };
                split = anchor.Split;
            }
            else
            {
                int appliedIndex;
                int appliedVersion;
                if (!RunStepEntity(m_BlueprintRun, anchor.CourseId, out appliedIndex, out appliedVersion))
                {
                    error = "step needs '" + anchor.CourseId + "' first; revise the sketch when the order cannot be satisfied";
                    return false;
                }
                entity = new Entity { Index = appliedIndex, Version = appliedVersion };
                split = anchor.Split;
            }
            if (!EntityManager.Exists(entity) || EntityManager.HasComponent<Deleted>(entity)
                || EntityManager.HasComponent<Temp>(entity))
            {
                error = "connection changed while building; inspect the site and resume the same revision";
                return false;
            }
            float3 actual;
            if (EntityManager.HasComponent<Node>(entity))
            {
                if (split != 0f)
                {
                    error = "a node connection does not accept split";
                    return false;
                }
                actual = EntityManager.GetComponentData<Node>(entity).m_Position;
            }
            else if (EntityManager.HasComponent<Edge>(entity) && EntityManager.HasComponent<Curve>(entity))
            {
                actual = Colossal.Mathematics.MathUtils.Position(
                    EntityManager.GetComponentData<Curve>(entity).m_Bezier, split);
            }
            else
            {
                error = "connection no longer identifies a road; inspect the site and resume the same revision";
                return false;
            }
            delta = actual - planned;
            float drift = math.length(delta);
            if (drift > kBlueprintAnchorDriftMax)
            {
                error = "built road drifted " + drift.ToString("F1") + "m from the reviewed layout; inspect before resuming";
                return false;
            }
            connection = new RoadConnection(entity, split);
            return true;
        }

        private static bool RunStepEntity(BlueprintRunRecord run, string courseId, out int index, out int version)
        {
            index = -1;
            version = 0;
            foreach (BlueprintStepRecord step in run.Steps)
            {
                if (step.CourseId == courseId && step.State == BlueprintStepState.Done && step.AppliedIndex >= 0)
                {
                    index = step.AppliedIndex;
                    version = step.AppliedVersion;
                    return true;
                }
            }
            return false;
        }

        private void ObserveBlueprintStep()
        {
            applyMode = ApplyMode.None;
            foreach (NetworkApplyTarget target in m_NetworkApplyTargets)
            {
                if (EntityManager.Exists(target.Preview)
                    && EntityManager.HasComponent<Temp>(target.Preview)
                    && !EntityManager.HasComponent<Deleted>(target.Preview))
                {
                    return;
                }
            }
            Entity main = Entity.Null;
            bool verified = true;
            foreach (NetworkApplyTarget target in m_NetworkApplyTargets)
            {
                if (target.Deleted)
                {
                    if (target.Original != Entity.Null && EntityManager.Exists(target.Original)
                        && !EntityManager.HasComponent<Deleted>(target.Original))
                    {
                        verified = false;
                    }
                    continue;
                }
                Entity entity = target.Target;
                if (!EntityManager.Exists(entity) || EntityManager.HasComponent<Temp>(entity)
                    || EntityManager.HasComponent<Deleted>(entity)
                    || !EntityManager.HasComponent<Edge>(entity) || !EntityManager.HasComponent<Curve>(entity))
                {
                    verified = false;
                    continue;
                }
                if (main == Entity.Null && target.Original == Entity.Null)
                {
                    main = entity;
                }
            }
            if (main == Entity.Null)
            {
                foreach (NetworkApplyTarget target in m_NetworkApplyTargets)
                {
                    if (!target.Deleted && EntityManager.Exists(target.Target)
                        && !EntityManager.HasComponent<Temp>(target.Target)
                        && !EntityManager.HasComponent<Deleted>(target.Target)
                        && EntityManager.HasComponent<Edge>(target.Target))
                    {
                        main = target.Target;
                        break;
                    }
                }
            }
            if (!verified || main == Entity.Null)
            {
                string note = "native construction could not be verified for step '" + m_BlueprintCourseId
                    + "'; inspect the site before resuming the same revision";
                NetworkBlueprintStore.MarkStepBlocked(m_BlueprintRun, m_BlueprintCourseId, note);
                CompletePending(BlueprintRunResponse("blocked", note));
                m_Stage = Stage.Finish;
                return;
            }
            NetworkBlueprintStore.MarkStepDone(m_BlueprintRun, m_BlueprintCourseId, main.Index, main.Version);
            m_BlueprintHeartbeatUtc = DateTime.UtcNow;
            Mod.Log.Info("blueprint run " + m_BlueprintRun.RunId + ": step " + m_BlueprintCourseId
                + " applied as " + main.Index + "v" + main.Version);
            if (m_BlueprintRun.InterruptRequested)
            {
                NetworkBlueprintStore.MarkInterrupted(m_BlueprintRun);
                CompletePending(BlueprintRunResponse("interrupted",
                    "stopped after the current road; resume the same revision to continue"));
                m_Stage = Stage.Finish;
                return;
            }
            string next = NetworkBlueprintStore.NextReadyStep(m_BlueprintRecord, m_BlueprintRun);
            if (next == null)
            {
                if (m_BlueprintRun.Status == BlueprintRunStatus.Completed)
                {
                    CompletePending(BlueprintRunResponse("completed",
                        "every road of the reviewed revision is built; verify flow with fresh reads after simulation moves"));
                }
                else
                {
                    CompletePending(BlueprintRunResponse("blocked",
                        "no buildable step remains while work is unfinished; inspect the run for the blocking step"));
                    m_BlueprintRun.Status = BlueprintRunStatus.Blocked;
                }
                m_Stage = Stage.Finish;
                return;
            }
            if (!QueueNextBlueprintStep())
            {
                m_Stage = Stage.Finish;
            }
        }

        private BridgeResponse BlueprintRunResponse(string status, string note)
        {
            var steps = new List<object>();
            int done = 0;
            foreach (BlueprintStepRecord step in m_BlueprintRun.Steps)
            {
                if (step.State == BlueprintStepState.Done)
                {
                    done++;
                }
                steps.Add(new
                {
                    course = step.CourseId,
                    state = step.State.ToString().ToLowerInvariant(),
                    applied = step.AppliedIndex >= 0
                        ? new { index = step.AppliedIndex, version = step.AppliedVersion }
                        : null,
                });
            }
            BridgeResponse response = BridgeResponse.Json(new
            {
                blueprint = m_BlueprintRecord.Id,
                version = m_BlueprintRecord.Version,
                run = m_BlueprintRun.RunId,
                status,
                done,
                total = m_BlueprintRun.Steps.Count,
                steps,
                note,
            });
            response.Success = status == "completed" || status == "interrupted";
            if (!response.Success)
            {
                response.ErrorKind = BridgeErrorKind.Conflict;
            }
            return response;
        }

        private void ClearBlueprintRun()
        {
            m_BlueprintRecord = null;
            m_BlueprintRun = null;
            m_BlueprintPrefabs = null;
            m_BlueprintCourseId = null;
        }
    }
}
