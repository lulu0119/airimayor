using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2MCP
{
    public sealed partial class BridgeToolSystem
    {
        private EntityQuery m_TempNetworkQuery;
        private CompiledRoadCourse m_CompiledRoadCourse;
        private readonly List<NetworkApplyTarget> m_NetworkApplyTargets = new List<NetworkApplyTarget>();

        private readonly struct NetworkApplyTarget
        {
            public NetworkApplyTarget(Entity preview, Temp temp)
            {
                Preview = preview;
                Original = temp.m_Original;
                Deleted = (temp.m_Flags & TempFlags.Delete) != 0;
                Target = Deleted ? Entity.Null
                    : temp.m_Original != Entity.Null && (temp.m_Flags & (TempFlags.Replace | TempFlags.Combine)) == 0
                        ? temp.m_Original : preview;
            }

            public Entity Preview { get; }
            public Entity Original { get; }
            public Entity Target { get; }
            public bool Deleted { get; }
        }

        internal bool TryQueueCompiledRoad(Entity prefabEntity, PrefabBase prefab,
            CompiledRoadCourse course, BridgeRequest request)
        {
            if (!TryQueueRoad(prefabEntity, prefab, course.Path.A, course.Path.D,
                default, false, course.Elevations, course.Mode, course.Path, request))
            {
                return false;
            }
            m_CompiledRoadCourse = course;
            return true;
        }

        private bool CaptureNetworkApplyTargets()
        {
            m_NetworkApplyTargets.Clear();
            using (NativeArray<Entity> previews = m_TempNetworkQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity preview in previews)
                {
                    m_NetworkApplyTargets.Add(new NetworkApplyTarget(preview,
                        EntityManager.GetComponentData<Temp>(preview)));
                }
            }
            return m_NetworkApplyTargets.Count != 0;
        }

        private void ObserveNetworkApply()
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

            var applied = new List<object>();
            bool verified = true;
            foreach (NetworkApplyTarget target in m_NetworkApplyTargets)
            {
                if (target.Deleted)
                {
                    bool removed = target.Original == Entity.Null || !EntityManager.Exists(target.Original)
                        || EntityManager.HasComponent<Deleted>(target.Original);
                    verified &= removed;
                    applied.Add(new { original = EntityIdentity(target.Original), removed });
                    continue;
                }
                Entity entity = target.Target;
                if (!EntityManager.Exists(entity) || EntityManager.HasComponent<Temp>(entity)
                    || EntityManager.HasComponent<Deleted>(entity)
                    || !EntityManager.HasComponent<Edge>(entity) || !EntityManager.HasComponent<Curve>(entity))
                {
                    verified = false;
                    applied.Add(new { original = EntityIdentity(target.Original), entity = EntityIdentity(entity), missing = true });
                    continue;
                }
                Edge edge = EntityManager.GetComponentData<Edge>(entity);
                Curve curve = EntityManager.GetComponentData<Curve>(entity);
                PrefabRef prefab = EntityManager.GetComponentData<PrefabRef>(entity);
                applied.Add(new
                {
                    original = EntityIdentity(target.Original), entity = EntityIdentity(entity),
                    prefab = EntityIdentity(prefab.m_Prefab),
                    start = EntityIdentity(edge.m_Start), end = EntityIdentity(edge.m_End),
                    curve = new { a = Position(curve.m_Bezier.a), b = Position(curve.m_Bezier.b),
                        c = Position(curve.m_Bezier.c), d = Position(curve.m_Bezier.d) },
                });
            }

            BridgeResponse response = BridgeResponse.Json(new
            {
                placed = verified,
                status = verified ? "applied" : "unverified",
                prefab = m_PendingPrefab.name,
                mode = DescribeRoadMode(m_PendingRoadMode),
                start = Position(m_PendingPosition), end = Position(m_PendingEnd),
                applied,
                note = verified ? "construction applied; returned roads include new roads and changed connection roads"
                    : "construction was submitted but some results could not be verified; inspect the site before building again",
            });
            response.Success = verified;
            if (!verified) response.ErrorKind = BridgeErrorKind.Conflict;
            Mod.Log.Info($"network apply: status={(verified ? "applied" : "unverified")}, tracked={applied.Count}");
            CompletePending(response);
            m_Stage = Stage.Finish;
        }

        private static object EntityIdentity(Entity entity) => entity == Entity.Null
            ? null : new { index = entity.Index, version = entity.Version };

        private static object Position(float3 point) => new { x = point.x, y = point.y, z = point.z };

        private void ResolveCourseConnection(RoadConnection connection, out Entity anchor, out float split)
        {
            anchor = connection.Entity;
            split = connection.Split;
            if (anchor != Entity.Null && EntityManager.HasComponent<Edge>(anchor))
            {
                Edge edge = EntityManager.GetComponentData<Edge>(anchor);
                PlacementSearchMath.ResolveConnectionAnchor(anchor, edge.m_Start, edge.m_End,
                    split, out anchor, out split);
            }
        }

        private static bool TryValidateCourseConnection(EntityManager manager,
            RoadConnection connection, float3 position, out string error)
        {
            error = null;
            if (connection.Entity == Entity.Null) return true;
            Entity entity = connection.Entity;
            if (!manager.Exists(entity) || manager.HasComponent<Game.Common.Deleted>(entity)
                || manager.HasComponent<Game.Tools.Temp>(entity))
            {
                error = "connection road changed; read the site again";
                return false;
            }
            float3 expected;
            if (manager.HasComponent<Node>(entity))
            {
                if (connection.Split != 0f)
                {
                    error = "a node connection does not accept split";
                    return false;
                }
                expected = manager.GetComponentData<Node>(entity).m_Position;
            }
            else if (manager.HasComponent<Edge>(entity) && manager.HasComponent<Curve>(entity))
            {
                expected = Colossal.Mathematics.MathUtils.Position(manager.GetComponentData<Curve>(entity).m_Bezier, connection.Split);
            }
            else
            {
                error = "connection must identify an existing road node or edge";
                return false;
            }
            if (math.distance(expected, position) <= 0.05f) return true;
            error = "course endpoint does not match the selected connection in x/y/z within 0.05 meters";
            return false;
        }
    }
}
