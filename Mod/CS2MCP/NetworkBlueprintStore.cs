using System;
using System.Collections.Generic;

namespace CS2MCP
{
    internal enum BlueprintRunStatus : byte
    {
        Building,
        Interrupted,
        Blocked,
        Completed,
    }

    internal enum BlueprintStepState : byte
    {
        Pending,
        Done,
        Blocked,
    }

    internal sealed class BlueprintRecord
    {
        public string Id;
        public int Version;
        public string SketchHash;
        public string SiteFingerprint;
        public string Status;
        public readonly List<ResolvedCourse> Courses = new List<ResolvedCourse>();
        public readonly List<BlueprintDiagnostic> Diagnostics = new List<BlueprintDiagnostic>();
        public readonly List<string> BuildOrder = new List<string>();
        public int RoadCount;
        public float TotalLength;
    }

    internal sealed class BlueprintStepRecord
    {
        public string CourseId;
        public BlueprintStepState State;
        public int AppliedIndex = -1;
        public int AppliedVersion;
        public string Note;
    }

    internal sealed class BlueprintRunRecord
    {
        public string RunId;
        public string BlueprintId;
        public int Version;
        public BlueprintRunStatus Status;
        public readonly List<BlueprintStepRecord> Steps = new List<BlueprintStepRecord>();
        public bool InterruptRequested;
    }

    /// <summary>
    /// Session registry for immutable blueprint revisions and their build
    /// runs. Revisions never change after planning; runs record per-step
    /// native results so interrupted work resumes instead of rebuilding.
    /// Fingerprints opaque to this module: the native adapter owns them.
    /// </summary>
    internal static class NetworkBlueprintStore
    {
        private const int kMaxBlueprints = 32;
        private static readonly Dictionary<string, List<BlueprintRecord>> s_Blueprints =
            new Dictionary<string, List<BlueprintRecord>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, BlueprintRunRecord> s_Runs =
            new Dictionary<string, BlueprintRunRecord>(StringComparer.Ordinal);
        private static int s_NextId = 1;

        public static void ClearForTests()
        {
            s_Blueprints.Clear();
            s_Runs.Clear();
            s_NextId = 1;
        }

        public static string HashSketch(string canonicalSketch)
        {
            if (string.IsNullOrEmpty(canonicalSketch))
            {
                return "empty";
            }
            unchecked
            {
                ulong hash = 1469598103934665603ul;
                foreach (char c in canonicalSketch)
                {
                    hash ^= c;
                    hash *= 1099511628211ul;
                }
                return hash.ToString("x16");
            }
        }

        public static BlueprintRecord Create(
            string sketchHash,
            string siteFingerprint,
            BlueprintPlanResult result)
        {
            string id = "bp" + s_NextId++;
            var record = new BlueprintRecord
            {
                Id = id,
                Version = 1,
                SketchHash = sketchHash ?? "empty",
                SiteFingerprint = siteFingerprint ?? string.Empty,
                Status = result.Status.ToString().ToLowerInvariant(),
                RoadCount = result.RoadCount,
                TotalLength = result.TotalLength,
            };
            record.Courses.AddRange(result.Courses);
            record.Diagnostics.AddRange(result.Diagnostics);
            record.BuildOrder.AddRange(result.BuildOrder);
            s_Blueprints[id] = new List<BlueprintRecord> { record };
            Trim();
            return record;
        }

        public static BlueprintRecord Revise(
            string blueprintId,
            string sketchHash,
            string siteFingerprint,
            BlueprintPlanResult result)
        {
            List<BlueprintRecord> versions;
            if (string.IsNullOrEmpty(blueprintId) || !s_Blueprints.TryGetValue(blueprintId, out versions))
            {
                return Create(sketchHash, siteFingerprint, result);
            }
            var record = new BlueprintRecord
            {
                Id = blueprintId,
                Version = versions[versions.Count - 1].Version + 1,
                SketchHash = sketchHash ?? "empty",
                SiteFingerprint = siteFingerprint ?? string.Empty,
                Status = result.Status.ToString().ToLowerInvariant(),
                RoadCount = result.RoadCount,
                TotalLength = result.TotalLength,
            };
            record.Courses.AddRange(result.Courses);
            record.Diagnostics.AddRange(result.Diagnostics);
            record.BuildOrder.AddRange(result.BuildOrder);
            versions.Add(record);
            return record;
        }

        public static BlueprintRecord Get(string blueprintId, int version)
        {
            List<BlueprintRecord> versions;
            if (string.IsNullOrEmpty(blueprintId) || !s_Blueprints.TryGetValue(blueprintId, out versions))
            {
                return null;
            }
            if (version <= 0)
            {
                return versions[versions.Count - 1];
            }
            foreach (BlueprintRecord record in versions)
            {
                if (record.Version == version)
                {
                    return record;
                }
            }
            return null;
        }

        public static bool IsCurrent(BlueprintRecord record, string siteFingerprint)
        {
            return record != null && string.Equals(record.SiteFingerprint, siteFingerprint ?? string.Empty, StringComparison.Ordinal);
        }

        public static BlueprintRunRecord GetOrCreateRun(BlueprintRecord record)
        {
            foreach (BlueprintRunRecord run in s_Runs.Values)
            {
                if (run.BlueprintId == record.Id && run.Version == record.Version)
                {
                    return run;
                }
            }
            var created = new BlueprintRunRecord
            {
                RunId = "run" + s_NextId++,
                BlueprintId = record.Id,
                Version = record.Version,
                Status = BlueprintRunStatus.Building,
            };
            foreach (string courseId in record.BuildOrder)
            {
                created.Steps.Add(new BlueprintStepRecord { CourseId = courseId });
            }
            s_Runs[created.RunId] = created;
            return created;
        }

        public static BlueprintRunRecord GetRun(string runId)
        {
            BlueprintRunRecord run;
            return s_Runs.TryGetValue(runId, out run) ? run : null;
        }

        public static string NextReadyStep(BlueprintRecord record, BlueprintRunRecord run)
        {
            var done = new HashSet<string>(StringComparer.Ordinal);
            foreach (BlueprintStepRecord step in run.Steps)
            {
                if (step.State == BlueprintStepState.Done)
                {
                    done.Add(step.CourseId);
                }
            }
            var courses = new Dictionary<string, ResolvedCourse>(StringComparer.Ordinal);
            foreach (ResolvedCourse course in record.Courses)
            {
                courses[course.Id] = course;
            }
            foreach (BlueprintStepRecord step in run.Steps)
            {
                if (step.State != BlueprintStepState.Pending)
                {
                    continue;
                }
                ResolvedCourse course;
                if (!courses.TryGetValue(step.CourseId, out course))
                {
                    continue;
                }
                if (AnchorDone(course.Start, done) && AnchorDone(course.End, done))
                {
                    return step.CourseId;
                }
            }
            return null;
        }

        public static void MarkStepDone(BlueprintRunRecord run, string courseId, int appliedIndex, int appliedVersion)
        {
            foreach (BlueprintStepRecord step in run.Steps)
            {
                if (step.CourseId == courseId)
                {
                    step.State = BlueprintStepState.Done;
                    step.AppliedIndex = appliedIndex;
                    step.AppliedVersion = appliedVersion;
                    break;
                }
            }
            if (AllDone(run))
            {
                run.Status = BlueprintRunStatus.Completed;
            }
        }

        public static void MarkStepBlocked(BlueprintRunRecord run, string courseId, string note)
        {
            foreach (BlueprintStepRecord step in run.Steps)
            {
                if (step.CourseId == courseId)
                {
                    step.State = BlueprintStepState.Blocked;
                    step.Note = note;
                    break;
                }
            }
            run.Status = BlueprintRunStatus.Blocked;
        }

        public static void RequestInterrupt(BlueprintRunRecord run)
        {
            run.InterruptRequested = true;
        }

        public static void RequestInterruptAll()
        {
            foreach (BlueprintRunRecord run in s_Runs.Values)
            {
                if (run.Status == BlueprintRunStatus.Building)
                {
                    run.InterruptRequested = true;
                }
            }
        }

        public static void MarkInterrupted(BlueprintRunRecord run)
        {
            if (run.Status == BlueprintRunStatus.Building)
            {
                run.Status = BlueprintRunStatus.Interrupted;
            }
        }

        private static bool AnchorDone(BlueprintAnchorRef anchor, HashSet<string> done)
        {
            return anchor.Kind != BlueprintAnchorKind.Blueprint || anchor.CourseId == null || done.Contains(anchor.CourseId);
        }

        private static bool AllDone(BlueprintRunRecord run)
        {
            foreach (BlueprintStepRecord step in run.Steps)
            {
                if (step.State != BlueprintStepState.Done)
                {
                    return false;
                }
            }
            return true;
        }

        private static void Trim()
        {
            if (s_Blueprints.Count <= kMaxBlueprints)
            {
                return;
            }
            var ids = new List<string>(s_Blueprints.Keys);
            ids.Sort(StringComparer.Ordinal);
            for (int i = 0; i < ids.Count - kMaxBlueprints; i++)
            {
                s_Blueprints.Remove(ids[i]);
            }
        }
    }
}
