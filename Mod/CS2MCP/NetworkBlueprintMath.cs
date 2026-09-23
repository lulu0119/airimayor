using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace CS2MCP
{
    /// <summary>
    /// Input limits for one road sketch. Over-limit sketches are rejected with
    /// a diagnostic instead of being silently truncated.
    /// </summary>
    internal static class NetworkBlueprintLimits
    {
        public const int MaxExplicitRoads = 64;
        public const int MaxDistricts = 8;
        public const int MaxCourses = 256;
        public const float MaxCourseLength = 1500f;
        public const float MinCourseLength = 8f;
    }

    internal enum BlueprintStatus : byte
    {
        Ready,
        Invalid,
        Unresolved,
    }

    /// <summary>
    /// Site facts the planner may sample. The native adapter implements this
    /// from terrain, water and owned-tile reads on the simulation thread;
    /// tests supply a flat fake. The planner never sees ECS.
    /// </summary>
    internal interface INetworkSiteSampler
    {
        float TerrainHeight(float x, float z);
        float WaterDepth(float x, float z);
        bool Owned(float x, float z);
    }

    internal enum SketchAnchorKind : byte
    {
        Free,
        ExistingNode,
        ExistingEdge,
        SketchRoad,
    }

    internal sealed class SketchAnchor
    {
        public string Id;
        public SketchAnchorKind Kind;
        public float X;
        public float Z;
        public int EntityIndex;
        public int EntityVersion;
        public float Split;
        public string RoadId;
        public bool HasTangent;
        public float TangentX;
        public float TangentZ;
    }

    internal sealed class SketchPoint
    {
        public float X;
        public float Z;

        public SketchPoint(float x, float z)
        {
            X = x;
            Z = z;
        }
    }

    internal sealed class SketchRoad
    {
        public string Id;
        public string Prefab;
        public string From;
        public string To;
        public readonly List<SketchPoint> Via = new List<SketchPoint>();
        public readonly List<SketchPoint> Through = new List<SketchPoint>();
        public float CorridorHalfWidth = 40f;
        public RoadBuildMode Mode = RoadBuildMode.Ground;
        public float E1;
        public float E2;
        public float Clearance = 6f;
        public readonly List<string> Structures = new List<string>();
        public float HalfWidth = 8f;
        public float MaxGrade = 0.10f;
        public float MinCurveRadius;
    }

    internal sealed class SketchCrossing
    {
        public string A;
        public string B;
        public string Kind = "at-grade";
        public string Upper;
    }

    internal sealed class SketchDistrict
    {
        public string Id;
        public float MinX;
        public float MinZ;
        public float MaxX;
        public float MaxZ;
        public string Field = "grid";
        public float AngleDeg;
        public float CenterX;
        public float CenterZ;
        public float Spacing = 80f;
        public string StreetPrefab;
        public float StreetHalfWidth = 6f;
        public float StreetMaxGrade = 0.10f;
        public int MaxStreets = 32;
        public int MinExits = 2;
        public bool AllowDeadEnds;
        public readonly List<string> ConnectTo = new List<string>();
    }

    internal sealed class SketchBlockedRect
    {
        public float MinX;
        public float MinZ;
        public float MaxX;
        public float MaxZ;
    }

    internal sealed class NetworkSketch
    {
        public float AreaMinX;
        public float AreaMinZ;
        public float AreaMaxX;
        public float AreaMaxZ;
        public readonly List<SketchAnchor> Anchors = new List<SketchAnchor>();
        public readonly List<SketchRoad> Roads = new List<SketchRoad>();
        public readonly List<SketchCrossing> Crossings = new List<SketchCrossing>();
        public readonly List<SketchDistrict> Districts = new List<SketchDistrict>();
        public readonly List<SketchBlockedRect> Blocked = new List<SketchBlockedRect>();
    }

    internal enum BlueprintAnchorKind : byte
    {
        None,
        Existing,
        Blueprint,
    }

    internal readonly struct BlueprintAnchorRef
    {
        public BlueprintAnchorRef(
            BlueprintAnchorKind kind,
            int entityIndex = 0,
            int entityVersion = 0,
            float split = 0f,
            string courseId = null)
        {
            Kind = kind;
            EntityIndex = entityIndex;
            EntityVersion = entityVersion;
            Split = split;
            CourseId = courseId;
        }

        public BlueprintAnchorKind Kind { get; }
        public int EntityIndex { get; }
        public int EntityVersion { get; }
        public float Split { get; }
        public string CourseId { get; }
    }

    /// <summary>One native build segment of a resolved blueprint.</summary>
    internal sealed class ResolvedCourse
    {
        public string Id;
        public string RoadId;
        public string Prefab;
        public RoadBuildMode Mode;
        public RoadPath Path;
        public float E1;
        public float E2;
        public BlueprintAnchorRef Start;
        public BlueprintAnchorRef End;
        public string DistrictId;
        public float Length;
        public float WidthM;
    }

    internal sealed class SolvedSketchRoad
    {
        public SketchRoad Sketch;
        public readonly List<float2> Center = new List<float2>();
        public readonly List<ResolvedCourse> Courses = new List<ResolvedCourse>();
    }

    internal sealed class BlueprintDiagnostic
    {
        public string Road;
        public string Type;
        public float AtX;
        public float AtZ;
        public bool HasAt;
        public float Observed;
        public float Allowed;
        public string Message;
    }

    internal sealed class BlueprintPlanResult
    {
        public BlueprintStatus Status;
        public readonly List<ResolvedCourse> Courses = new List<ResolvedCourse>();
        public readonly List<BlueprintDiagnostic> Diagnostics = new List<BlueprintDiagnostic>();
        public readonly List<string> BuildOrder = new List<string>();
        public int RoadCount;
        public float TotalLength;
    }

    /// <summary>
    /// Deterministic sketch-to-blueprint solver. Fixed inputs and sampler
    /// produce identical outputs; bounded search reports unresolved instead of
    /// searching without end. This module owns topology ordering, corridor
    /// search, curve fitting, vertical profiles, crossing checks and the
    /// resulting native course list.
    /// </summary>
    internal static class NetworkBlueprintPlanner
    {
        private const float kSearchCell = 8f;
        private const float kFitSample = 4f;
        private const int kSearchBudgetPerLeg = 40000;
        private const float kThroughTolerance = 6f;
        private const float kWaterDepthBlock = 0.2f;
        private const float kMaxChunkLength = 1200f;
        private const float kClearanceBumpHalfLength = 50f;

        public static BlueprintPlanResult Plan(NetworkSketch sketch, INetworkSiteSampler sampler)
        {
            var result = new BlueprintPlanResult();
            if (sketch == null || sampler == null)
            {
                AddDiagnostic(result, null, "invalid", "sketch and site are required");
                result.Status = BlueprintStatus.Invalid;
                return result;
            }
            if (!ValidateSketch(sketch, result))
            {
                result.Status = BlueprintStatus.Invalid;
                return result;
            }
            var anchors = new Dictionary<string, SketchAnchor>(StringComparer.Ordinal);
            foreach (SketchAnchor anchor in sketch.Anchors)
            {
                anchors[anchor.Id] = anchor;
            }
            var roads = new Dictionary<string, SketchRoad>(StringComparer.Ordinal);
            foreach (SketchRoad road in sketch.Roads)
            {
                roads[road.Id] = road;
            }
            List<string> order = TopoOrder(sketch, result);
            if (order == null)
            {
                result.Status = BlueprintStatus.Invalid;
                return result;
            }
            var solved = new Dictionary<string, SolvedSketchRoad>(StringComparer.Ordinal);
            bool unresolved = false;
            foreach (string roadId in order)
            {
                SketchRoad road = roads[roadId];
                if (!SolveRoad(sketch, anchors, roads, solved, road, sampler, result))
                {
                    if (result.Status == BlueprintStatus.Invalid)
                    {
                        return result;
                    }
                    unresolved = true;
                    break;
                }
            }
            if (unresolved)
            {
                result.Status = BlueprintStatus.Unresolved;
                return result;
            }
            if (!CheckCrossings(sketch, solved, result))
            {
                result.Status = BlueprintStatus.Invalid;
                return result;
            }
            if (!NetworkBlueprintDistricts.Generate(sketch, solved, sampler, result))
            {
                return result;
            }
            if (result.Courses.Count > NetworkBlueprintLimits.MaxCourses)
            {
                AddDiagnostic(result, null, "limit",
                    "resolved " + result.Courses.Count + " courses (max " + NetworkBlueprintLimits.MaxCourses + "); shrink the area or split the sketch");
                result.Status = BlueprintStatus.Invalid;
                return result;
            }
            foreach (ResolvedCourse course in result.Courses)
            {
                result.BuildOrder.Add(course.Id);
                result.TotalLength += course.Length;
            }
            result.RoadCount = solved.Count;
            result.Status = BlueprintStatus.Ready;
            return result;
        }

        internal static float2 CubicPoint(RoadPath path, float t)
        {
            float inverse = 1f - t;
            float a = inverse * inverse * inverse;
            float b = 3f * inverse * inverse * t;
            float c = 3f * inverse * t * t;
            float d = t * t * t;
            return new float2(
                path.A.x * a + path.B.x * b + path.C.x * c + path.D.x * d,
                path.A.z * a + path.B.z * b + path.C.z * c + path.D.z * d);
        }

        internal static float CubicHeight(RoadPath path, float t)
        {
            float inverse = 1f - t;
            float a = inverse * inverse * inverse;
            float b = 3f * inverse * inverse * t;
            float c = 3f * inverse * t * t;
            float d = t * t * t;
            return path.A.y * a + path.B.y * b + path.C.y * c + path.D.y * d;
        }

        internal static List<float2> SampleCenter(RoadPath path)
        {
            float approx = math.distance(path.A, path.B)
                + math.distance(path.B, path.C) + math.distance(path.C, path.D);
            int count = math.max(2, (int)math.ceil(approx / kFitSample) + 1);
            var points = new List<float2>(count);
            for (int i = 0; i < count; i++)
            {
                points.Add(CubicPoint(path, count == 1 ? 0f : i / (float)(count - 1)));
            }
            return points;
        }

        private static bool ValidateSketch(NetworkSketch sketch, BlueprintPlanResult result)
        {
            if (sketch.AreaMaxX <= sketch.AreaMinX || sketch.AreaMaxZ <= sketch.AreaMinZ)
            {
                AddDiagnostic(result, null, "invalid", "planning area must have positive size");
                return false;
            }
            if (sketch.Roads.Count > NetworkBlueprintLimits.MaxExplicitRoads)
            {
                AddDiagnostic(result, null, "limit",
                    "sketch has " + sketch.Roads.Count + " roads (max " + NetworkBlueprintLimits.MaxExplicitRoads + "); split the scheme");
                return false;
            }
            if (sketch.Districts.Count > NetworkBlueprintLimits.MaxDistricts)
            {
                AddDiagnostic(result, null, "limit",
                    "sketch has " + sketch.Districts.Count + " districts (max " + NetworkBlueprintLimits.MaxDistricts + "); split the scheme");
                return false;
            }
            if (sketch.Roads.Count == 0 && sketch.Districts.Count == 0)
            {
                AddDiagnostic(result, null, "invalid", "sketch needs at least one road or district");
                return false;
            }
            var anchorIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (SketchAnchor anchor in sketch.Anchors)
            {
                if (string.IsNullOrEmpty(anchor.Id) || !anchorIds.Add(anchor.Id))
                {
                    AddDiagnostic(result, null, "invalid", "anchor ids must be unique and nonempty");
                    return false;
                }
            }
            var roadIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (SketchRoad road in sketch.Roads)
            {
                if (string.IsNullOrEmpty(road.Id) || !roadIds.Add(road.Id))
                {
                    AddDiagnostic(result, road.Id, "invalid", "road ids must be unique and nonempty");
                    return false;
                }
                if (string.IsNullOrEmpty(road.Prefab))
                {
                    AddDiagnostic(result, road.Id, "invalid", "road needs an exact prefab");
                    return false;
                }
                if (!anchorIds.Contains(road.From) || !anchorIds.Contains(road.To))
                {
                    AddDiagnostic(result, road.Id, "invalid", "endpoints must reference known anchors");
                    return false;
                }
                if (road.CorridorHalfWidth < 8f || road.CorridorHalfWidth > 500f)
                {
                    AddDiagnostic(result, road.Id, "invalid", "corridor half width must be 8..500 meters");
                    return false;
                }
                if (road.Mode == RoadBuildMode.Ground && (road.E1 != 0f || road.E2 != 0f))
                {
                    AddDiagnostic(result, road.Id, "invalid", "ground roads carry no endpoint heights; use grade-separated for structures");
                    return false;
                }
                if (road.Mode == RoadBuildMode.GradeSeparated && road.E1 == 0f && road.E2 == 0f)
                {
                    AddDiagnostic(result, road.Id, "invalid", "grade-separated roads need a nonzero endpoint height");
                    return false;
                }
                if (road.E1 < -30f || road.E1 > 60f || road.E2 < -30f || road.E2 > 60f)
                {
                    AddDiagnostic(result, road.Id, "invalid", "endpoint heights must stay in -30..60 meters relative to terrain");
                    return false;
                }
                if (road.HalfWidth <= 0f || road.HalfWidth > 40f)
                {
                    AddDiagnostic(result, road.Id, "invalid", "road half width must be positive and within 40 meters");
                    return false;
                }
                if (road.MaxGrade <= 0f || road.MaxGrade > 0.25f)
                {
                    AddDiagnostic(result, road.Id, "invalid", "maximum grade must be positive and within 25%");
                    return false;
                }
            }
            var districtIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (SketchDistrict district in sketch.Districts)
            {
                if (string.IsNullOrEmpty(district.Id) || !districtIds.Add(district.Id))
                {
                    AddDiagnostic(result, null, "invalid", "district ids must be unique and nonempty");
                    return false;
                }
                if (district.MaxX <= district.MinX || district.MaxZ <= district.MinZ)
                {
                    AddDiagnostic(result, district.Id, "invalid", "district rect must have positive size");
                    return false;
                }
                if (string.IsNullOrEmpty(district.StreetPrefab))
                {
                    AddDiagnostic(result, district.Id, "invalid", "district needs an exact street prefab");
                    return false;
                }
                if (district.Spacing < 24f || district.Spacing > 400f)
                {
                    AddDiagnostic(result, district.Id, "invalid", "street spacing must be 24..400 meters");
                    return false;
                }
                if (district.MaxStreets <= 0 || district.MaxStreets > 128)
                {
                    AddDiagnostic(result, district.Id, "invalid", "district street cap must be 1..128");
                    return false;
                }
                if (district.MinExits < 1 || district.MinExits > 8)
                {
                    AddDiagnostic(result, district.Id, "invalid", "required exits must be 1..8");
                    return false;
                }
                string field = district.Field == null ? "grid" : district.Field.ToLowerInvariant();
                if (field != "grid" && field != "radial" && field != "circular" && field != "mixed")
                {
                    AddDiagnostic(result, district.Id, "invalid", "direction field must be grid, radial, circular or mixed");
                    return false;
                }
                district.Field = field;
            }
            return true;
        }

        private static List<string> TopoOrder(NetworkSketch sketch, BlueprintPlanResult result)
        {
            var roads = new Dictionary<string, SketchRoad>(StringComparer.Ordinal);
            foreach (SketchRoad road in sketch.Roads)
            {
                roads[road.Id] = road;
            }
            var anchors = new Dictionary<string, SketchAnchor>(StringComparer.Ordinal);
            foreach (SketchAnchor anchor in sketch.Anchors)
            {
                anchors[anchor.Id] = anchor;
            }
            var depends = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var dependents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (SketchRoad road in sketch.Roads)
            {
                depends[road.Id] = new HashSet<string>(StringComparer.Ordinal);
                dependents[road.Id] = new List<string>();
            }
            foreach (SketchRoad road in sketch.Roads)
            {
                foreach (string endpoint in new[] { road.From, road.To })
                {
                    SketchAnchor anchor;
                    if (!anchors.TryGetValue(endpoint, out anchor))
                    {
                        continue;
                    }
                    if (anchor.Kind == SketchAnchorKind.SketchRoad && !string.IsNullOrEmpty(anchor.RoadId))
                    {
                        if (!roads.ContainsKey(anchor.RoadId))
                        {
                            AddDiagnostic(result, road.Id, "invalid", "anchor references unknown sketch road '" + anchor.RoadId + "'");
                            return null;
                        }
                        if (anchor.RoadId == road.Id)
                        {
                            AddDiagnostic(result, road.Id, "invalid", "road cannot depend on its own geometry");
                            return null;
                        }
                        if (depends[road.Id].Add(anchor.RoadId))
                        {
                            dependents[anchor.RoadId].Add(road.Id);
                        }
                    }
                }
            }
            var ready = new List<string>();
            foreach (SketchRoad road in sketch.Roads)
            {
                if (depends[road.Id].Count == 0)
                {
                    ready.Add(road.Id);
                }
            }
            ready.Sort(StringComparer.Ordinal);
            var order = new List<string>(sketch.Roads.Count);
            while (ready.Count > 0)
            {
                string next = ready[0];
                ready.RemoveAt(0);
                order.Add(next);
                List<string> waiting = dependents[next];
                waiting.Sort(StringComparer.Ordinal);
                foreach (string dependent in waiting)
                {
                    depends[dependent].Remove(next);
                    if (depends[dependent].Count == 0)
                    {
                        ready.Add(dependent);
                    }
                }
                ready.Sort(StringComparer.Ordinal);
            }
            if (order.Count != sketch.Roads.Count)
            {
                var stuck = new List<string>();
                foreach (SketchRoad road in sketch.Roads)
                {
                    if (depends[road.Id].Count > 0)
                    {
                        stuck.Add(road.Id);
                    }
                }
                stuck.Sort(StringComparer.Ordinal);
                AddDiagnostic(result, stuck.Count > 0 ? stuck[0] : null, "invalid",
                    "sketch roads have a circular geometry dependency: " + string.Join(", ", stuck.ToArray()));
                return null;
            }
            return order;
        }

        private static bool SolveRoad(
            NetworkSketch sketch,
            Dictionary<string, SketchAnchor> anchors,
            Dictionary<string, SketchRoad> roads,
            Dictionary<string, SolvedSketchRoad> solved,
            SketchRoad road,
            INetworkSiteSampler sampler,
            BlueprintPlanResult result)
        {
            float2 start;
            float2 end;
            if (!AnchorPosition(anchors, solved, road.From, result, road.Id, out start)
                || !AnchorPosition(anchors, solved, road.To, result, road.Id, out end))
            {
                result.Status = BlueprintStatus.Invalid;
                return false;
            }
            if (math.distance(start, end) < NetworkBlueprintLimits.MinCourseLength)
            {
                AddDiagnostic(result, road.Id, "invalid", "endpoints are closer than 8 meters");
                result.Status = BlueprintStatus.Invalid;
                return false;
            }
            var guide = new List<float2>();
            guide.Add(start);
            foreach (SketchPoint point in road.Via)
            {
                guide.Add(new float2(point.X, point.Z));
            }
            foreach (SketchPoint point in road.Through)
            {
                guide.Add(new float2(point.X, point.Z));
            }
            guide.Add(end);
            var hard = new List<float2>();
            hard.Add(start);
            foreach (SketchPoint point in road.Through)
            {
                hard.Add(new float2(point.X, point.Z));
            }
            hard.Add(end);
            var waypoints = new List<float2>();
            var splits = new List<int>();
            for (int leg = 0; leg + 1 < hard.Count; leg++)
            {
                List<float2> legPath;
                float2 legTangent;
                if (!SearchLeg(sketch, road, sampler, hard[leg], hard[leg + 1], guide,
                    out legPath, out legTangent, result))
                {
                    return false;
                }
                for (int i = 0; i < legPath.Count; i++)
                {
                    if (leg > 0 && i == 0)
                    {
                        continue;
                    }
                    waypoints.Add(legPath[i]);
                }
                if (leg > 0)
                {
                    splits.Add(waypoints.Count - legPath.Count);
                }
            }
            foreach (SketchPoint via in road.Via)
            {
                int nearest = NearestWaypoint(waypoints, new float2(via.X, via.Z));
                if (nearest > 0 && nearest < waypoints.Count - 1 && !splits.Contains(nearest))
                {
                    splits.Add(nearest);
                }
            }
            splits.Sort();
            List<RoadPath> shapes = FitChunks(anchors, road, waypoints, splits);
            if (shapes == null || shapes.Count == 0)
            {
                AddDiagnostic(result, road.Id, "unresolved", "corridor search found no fittable line");
                result.Status = BlueprintStatus.Unresolved;
                return false;
            }
            var solvedRoad = new SolvedSketchRoad { Sketch = road };
            var profile = new VerticalProfile(road, sampler, waypoints);
            for (int i = 0; i < shapes.Count; i++)
            {
                RoadPath flat = shapes[i];
                RoadPath lifted = profile.Lift(flat);
                float length = CourseLength(lifted);
                if (length < NetworkBlueprintLimits.MinCourseLength - 0.01f)
                {
                    continue;
                }
                if (length > NetworkBlueprintLimits.MaxCourseLength + 0.01f)
                {
                    AddDiagnostic(result, road.Id, "unresolved",
                        "fitted segment is " + length.ToString("F0") + "m (max 1500m); add through points to split it");
                    result.Status = BlueprintStatus.Unresolved;
                    return false;
                }
                if (!ValidateCourse(sketch, road, sampler, lifted, result))
                {
                    result.Status = BlueprintStatus.Invalid;
                    return false;
                }
                var course = new ResolvedCourse
                {
                    Id = road.Id + "#" + i,
                    RoadId = road.Id,
                    Prefab = road.Prefab,
                    Mode = road.Mode,
                    Path = lifted,
                    E1 = lifted.A.y - sampler.TerrainHeight(lifted.A.x, lifted.A.z),
                    E2 = lifted.D.y - sampler.TerrainHeight(lifted.D.x, lifted.D.z),
                    Length = length,
                    WidthM = road.HalfWidth * 2f,
                };
                if (i == 0)
                {
                    course.Start = EndAnchorRef(anchors, road.From);
                }
                else
                {
                    course.Start = new BlueprintAnchorRef(BlueprintAnchorKind.Blueprint, courseId: road.Id + "#" + (i - 1), split: 1f);
                }
                if (i == shapes.Count - 1)
                {
                    course.End = EndAnchorRef(anchors, road.To);
                }
                else
                {
                    course.End = new BlueprintAnchorRef(BlueprintAnchorKind.None);
                }
                solvedRoad.Courses.Add(course);
                result.Courses.Add(course);
            }
            if (solvedRoad.Courses.Count == 0)
            {
                AddDiagnostic(result, road.Id, "invalid", "road resolved to no buildable segment");
                result.Status = BlueprintStatus.Invalid;
                return false;
            }
            List<float2> center = SampleCenter(solvedRoad.Courses[0].Path);
            solvedRoad.Center.AddRange(center);
            for (int i = 1; i < solvedRoad.Courses.Count; i++)
            {
                List<float2> more = SampleCenter(solvedRoad.Courses[i].Path);
                for (int j = 1; j < more.Count; j++)
                {
                    solvedRoad.Center.Add(more[j]);
                }
            }
            solved[road.Id] = solvedRoad;
            ApplyClearanceBumps(sketch, roads, solved, sampler, result);
            if (result.Status == BlueprintStatus.Invalid)
            {
                return false;
            }
            return true;
        }

        private static BlueprintAnchorRef EndAnchorRef(Dictionary<string, SketchAnchor> anchors, string anchorId)
        {
            SketchAnchor anchor;
            if (!anchors.TryGetValue(anchorId, out anchor))
            {
                return new BlueprintAnchorRef(BlueprintAnchorKind.None);
            }
            if (anchor.Kind == SketchAnchorKind.ExistingNode || anchor.Kind == SketchAnchorKind.ExistingEdge)
            {
                return new BlueprintAnchorRef(BlueprintAnchorKind.Existing, anchor.EntityIndex, anchor.EntityVersion, anchor.Split);
            }
            return new BlueprintAnchorRef(BlueprintAnchorKind.None);
        }

        private static bool AnchorPosition(
            Dictionary<string, SketchAnchor> anchors,
            Dictionary<string, SolvedSketchRoad> solved,
            string anchorId,
            BlueprintPlanResult result,
            string roadId,
            out float2 position)
        {
            position = default;
            SketchAnchor anchor;
            if (!anchors.TryGetValue(anchorId, out anchor))
            {
                AddDiagnostic(result, roadId, "invalid", "unknown anchor '" + anchorId + "'");
                return false;
            }
            if (anchor.Kind == SketchAnchorKind.SketchRoad)
            {
                SolvedSketchRoad host;
                if (string.IsNullOrEmpty(anchor.RoadId) || !solved.TryGetValue(anchor.RoadId, out host))
                {
                    AddDiagnostic(result, roadId, "invalid", "anchor road '" + anchor.RoadId + "' is not solved yet");
                    return false;
                }
                float t = math.clamp(anchor.Split, 0f, 1f);
                position = PointOnSolved(host, t);
                return true;
            }
            position = new float2(anchor.X, anchor.Z);
            return true;
        }

        private static float2 PointOnSolved(SolvedSketchRoad host, float t)
        {
            if (host.Center.Count == 0)
            {
                return default;
            }
            float at = t * (host.Center.Count - 1);
            int lower = (int)math.floor(at);
            if (lower < 0)
            {
                lower = 0;
            }
            if (lower >= host.Center.Count - 1)
            {
                return host.Center[host.Center.Count - 1];
            }
            return math.lerp(host.Center[lower], host.Center[lower + 1], at - lower);
        }

        private static bool SearchLeg(
            NetworkSketch sketch,
            SketchRoad road,
            INetworkSiteSampler sampler,
            float2 start,
            float2 end,
            List<float2> guide,
            out List<float2> path,
            out float2 endTangent,
            BlueprintPlanResult result)
        {
            path = null;
            endTangent = end - start;
            float2 initial = InitialTangent(guide, start);
            float2 goalTangent = GoalTangent(guide, end);
            var open = new SearchHeap();
            var best = new Dictionary<long, float>();
            long startKey = StateKey(WorldToCell(start), HeadingOf(initial));
            open.Push(new SearchNode(WorldToCell(start), HeadingOf(initial), 0f, Heuristic(start, end), null, start));
            best[startKey] = 0f;
            int pops = 0;
            SearchNode goal = null;
            float margin = road.CorridorHalfWidth + road.HalfWidth + 32f;
            float minX = math.min(sketch.AreaMinX, math.min(start.x, end.x)) - margin;
            float minZ = math.min(sketch.AreaMinZ, math.min(start.y, end.y)) - margin;
            float maxX = math.max(sketch.AreaMaxX, math.max(start.x, end.x)) + margin;
            float maxZ = math.max(sketch.AreaMaxZ, math.max(start.y, end.y)) + margin;
            float2[] headings = Headings();
            while (open.Count > 0)
            {
                if (pops++ > kSearchBudgetPerLeg)
                {
                    AddDiagnostic(result, road.Id, "unresolved",
                        "no route found within the search budget near (" + end.x.ToString("F0") + ", " + end.y.ToString("F0") + "); widen the corridor or move through points");
                    result.Status = BlueprintStatus.Unresolved;
                    return false;
                }
                SearchNode current = open.Pop();
                long key = StateKey(current.Cell, current.Heading);
                float known;
                if (best.TryGetValue(key, out known) && current.Cost > known + 0.001f)
                {
                    continue;
                }
                if (math.distance(current.Position, end) <= 10f
                    && math.dot(headings[current.Heading], goalTangent) > 0.7f)
                {
                    goal = current;
                    break;
                }
                for (int h = 0; h < 8; h++)
                {
                    int turn = math.abs(h - current.Heading);
                    if (turn > 4)
                    {
                        turn = 8 - turn;
                    }
                    if (turn > 2)
                    {
                        continue;
                    }
                    float2 next = current.Position + headings[h] * kSearchCell;
                    if (next.x < minX || next.x > maxX || next.y < minZ || next.y > maxZ)
                    {
                        continue;
                    }
                    if (!CellAllowed(sketch, road, sampler, next))
                    {
                        continue;
                    }
                    float deviation = DistanceToGuide(guide, next);
                    if (deviation > road.CorridorHalfWidth + road.HalfWidth)
                    {
                        continue;
                    }
                    float slope = math.abs(sampler.TerrainHeight(next.x, next.y) - sampler.TerrainHeight(current.Position.x, current.Position.y)) / kSearchCell;
                    float step = kSearchCell + 6f * deviation + turn * 6f + math.min(slope * 40f, 40f);
                    float cost = current.Cost + step;
                    int2 cell = WorldToCell(next);
                    long nextKey = StateKey(cell, h);
                    float previous;
                    if (best.TryGetValue(nextKey, out previous) && cost >= previous)
                    {
                        continue;
                    }
                    best[nextKey] = cost;
                    open.Push(new SearchNode(cell, h, cost, cost + Heuristic(next, end), current, next));
                }
            }
            if (goal == null)
            {
                AddDiagnostic(result, road.Id, "unresolved",
                    "no route found near (" + end.x.ToString("F0") + ", " + end.y.ToString("F0") + "); widen the corridor or move through points");
                result.Status = BlueprintStatus.Unresolved;
                return false;
            }
            var reversed = new List<float2>();
            for (SearchNode node = goal; node != null; node = node.Parent)
            {
                reversed.Add(node.Position);
            }
            reversed.Reverse();
            reversed.Add(end);
            path = DouglasPeucker(reversed, 2f);
            endTangent = goalTangent;
            return true;
        }

        private static bool CellAllowed(NetworkSketch sketch, SketchRoad road, INetworkSiteSampler sampler, float2 point)
        {
            foreach (SketchBlockedRect blocked in sketch.Blocked)
            {
                if (point.x >= blocked.MinX - road.HalfWidth && point.x <= blocked.MaxX + road.HalfWidth
                    && point.y >= blocked.MinZ - road.HalfWidth && point.y <= blocked.MaxZ + road.HalfWidth)
                {
                    return false;
                }
            }
            if (!sampler.Owned(point.x, point.y))
            {
                return false;
            }
            if (road.Mode == RoadBuildMode.Ground && sampler.WaterDepth(point.x, point.y) >= kWaterDepthBlock)
            {
                return false;
            }
            return true;
        }

        private static float DistanceToGuide(List<float2> guide, float2 point)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < guide.Count; i++)
            {
                float2 ab = guide[i + 1] - guide[i];
                float lengthSq = math.lengthsq(ab);
                float t = lengthSq > 0.0001f ? math.clamp(math.dot(point - guide[i], ab) / lengthSq, 0f, 1f) : 0f;
                float distance = math.distance(point, guide[i] + ab * t);
                if (distance < best)
                {
                    best = distance;
                }
            }
            return best;
        }

        private static List<float2> DouglasPeucker(List<float2> points, float tolerance)
        {
            if (points.Count <= 2)
            {
                return new List<float2>(points);
            }
            var kept = new List<float2>();
            kept.Add(points[0]);
            SimplifyRange(points, 0, points.Count - 1, tolerance, kept);
            kept.Add(points[points.Count - 1]);
            return kept;
        }

        private static void SimplifyRange(List<float2> points, int first, int last, float tolerance, List<float2> kept)
        {
            if (last <= first + 1)
            {
                return;
            }
            float2 a = points[first];
            float2 b = points[last];
            float2 ab = b - a;
            float length = math.length(ab);
            float worst = -1f;
            int index = -1;
            for (int i = first + 1; i < last; i++)
            {
                float distance = length > 0.001f
                    ? math.abs((points[i].x - a.x) * ab.y - (points[i].y - a.y) * ab.x) / length
                    : math.distance(points[i], a);
                if (distance > worst)
                {
                    worst = distance;
                    index = i;
                }
            }
            if (worst > tolerance && index > 0)
            {
                SimplifyRange(points, first, index, tolerance, kept);
                kept.Add(points[index]);
                SimplifyRange(points, index, last, tolerance, kept);
            }
        }

        private static List<RoadPath> FitChunks(
            Dictionary<string, SketchAnchor> anchors, SketchRoad road, List<float2> waypoints, List<int> splits)
        {
            if (waypoints.Count < 2)
            {
                return null;
            }
            var bounds = new List<int> { 0 };
            foreach (int split in splits)
            {
                if (split > bounds[bounds.Count - 1] + 1 && split < waypoints.Count - 1)
                {
                    bounds.Add(split);
                }
            }
            bounds.Add(waypoints.Count - 1);
            var paths = new List<RoadPath>();
            for (int section = 0; section + 1 < bounds.Count; section++)
            {
                FitSection(anchors, road, waypoints, bounds[section], bounds[section + 1], paths);
            }
            return paths;
        }

        private static void FitSection(
            Dictionary<string, SketchAnchor> anchors, SketchRoad road, List<float2> waypoints,
            int first, int last, List<RoadPath> paths)
        {
            float total = 0f;
            for (int i = first; i < last; i++)
            {
                total += math.distance(waypoints[i], waypoints[i + 1]);
            }
            int chunks = math.max(1, (int)math.ceil(total / kMaxChunkLength));
            for (int chunk = 0; chunk < chunks; chunk++)
            {
                float from = first + (last - first) * chunk / (float)chunks;
                float to = first + (last - first) * (chunk + 1) / (float)chunks;
                float2 p0 = PointAtIndex(waypoints, from);
                float2 p3 = PointAtIndex(waypoints, to);
                float2 t0 = TangentAtIndex(anchors, road, waypoints, first, last, from, true);
                float2 t3 = TangentAtIndex(anchors, road, waypoints, first, last, to, false);
                float chord = math.max(math.distance(p0, p3), 1f);
                float scale = math.clamp(chord / 3f, 10f, 500f);
                paths.Add(new RoadPath(
                    new float3(p0.x, 0f, p0.y),
                    new float3(p0.x + t0.x * scale, 0f, p0.y + t0.y * scale),
                    new float3(p3.x - t3.x * scale, 0f, p3.y - t3.y * scale),
                    new float3(p3.x, 0f, p3.y)));
            }
        }

        private static int NearestWaypoint(List<float2> waypoints, float2 point)
        {
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < waypoints.Count; i++)
            {
                float distance = math.distancesq(waypoints[i], point);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            return best;
        }

        private static float2 PointAtIndex(List<float2> waypoints, float at)
        {
            int lower = (int)math.floor(at);
            if (lower < 0)
            {
                lower = 0;
            }
            if (lower >= waypoints.Count - 1)
            {
                return waypoints[waypoints.Count - 1];
            }
            return math.lerp(waypoints[lower], waypoints[lower + 1], math.clamp(at - lower, 0f, 1f));
        }

        private static float2 TangentAtIndex(
            Dictionary<string, SketchAnchor> anchors, SketchRoad road, List<float2> waypoints,
            int first, int last, float at, bool isStart)
        {
            if (isStart && at <= first + 0.01f && first == 0)
            {
                SketchAnchor fromAnchor;
                if (anchors.TryGetValue(road.From, out fromAnchor) && fromAnchor.HasTangent)
                {
                    float2 explicitFrom = new float2(fromAnchor.TangentX, fromAnchor.TangentZ);
                    if (math.all(math.isfinite(explicitFrom)) && math.lengthsq(explicitFrom) > 0.0001f)
                    {
                        return math.normalize(explicitFrom);
                    }
                }
            }
            if (!isStart && at >= last - 0.01f && last == waypoints.Count - 1)
            {
                SketchAnchor toAnchor;
                if (anchors.TryGetValue(road.To, out toAnchor) && toAnchor.HasTangent)
                {
                    float2 explicitTo = new float2(toAnchor.TangentX, toAnchor.TangentZ);
                    if (math.all(math.isfinite(explicitTo)) && math.lengthsq(explicitTo) > 0.0001f)
                    {
                        return math.normalize(explicitTo);
                    }
                }
            }
            float ahead = isStart ? math.min(at + 1f, last) : math.max(at - 1f, first);
            float2 here = PointAtIndex(waypoints, at);
            float2 other = PointAtIndex(waypoints, ahead);
            float2 tangent = isStart ? other - here : here - other;
            if (math.lengthsq(tangent) < 0.0001f)
            {
                tangent = waypoints[last] - waypoints[first];
            }
            if (math.lengthsq(tangent) < 0.0001f)
            {
                return new float2(1f, 0f);
            }
            return math.normalize(tangent);
        }

        private static float2 InitialTangent(List<float2> guide, float2 start)
        {
            for (int i = 0; i + 1 < guide.Count; i++)
            {
                float2 delta = guide[i + 1] - guide[i];
                if (math.lengthsq(delta) > 0.0001f)
                {
                    return math.normalize(delta);
                }
            }
            return new float2(1f, 0f);
        }

        private static float2 GoalTangent(List<float2> guide, float2 end)
        {
            for (int i = guide.Count - 1; i > 0; i--)
            {
                float2 delta = guide[i] - guide[i - 1];
                if (math.lengthsq(delta) > 0.0001f)
                {
                    return math.normalize(delta);
                }
            }
            return new float2(1f, 0f);
        }

        private static float2[] Headings()
        {
            return new[]
            {
                new float2(1f, 0f),
                new float2(0.7071068f, 0.7071068f),
                new float2(0f, 1f),
                new float2(-0.7071068f, 0.7071068f),
                new float2(-1f, 0f),
                new float2(-0.7071068f, -0.7071068f),
                new float2(0f, -1f),
                new float2(0.7071068f, -0.7071068f),
            };
        }

        private static int HeadingOf(float2 direction)
        {
            float2[] headings = Headings();
            int best = 0;
            float score = math.dot(direction, headings[0]);
            for (int i = 1; i < headings.Length; i++)
            {
                float candidate = math.dot(direction, headings[i]);
                if (candidate > score)
                {
                    score = candidate;
                    best = i;
                }
            }
            return best;
        }

        private static int2 WorldToCell(float2 point)
        {
            return new int2((int)math.floor(point.x / kSearchCell), (int)math.floor(point.y / kSearchCell));
        }

        private static long StateKey(int2 cell, int heading)
        {
            return ((long)(cell.x + 200000) << 32) | ((long)(uint)(cell.y + 200000) << 3) | (long)(uint)heading;
        }

        private static float Heuristic(float2 from, float2 to)
        {
            return math.distance(from, to);
        }

        private sealed class SearchNode
        {
            public SearchNode(int2 cell, int heading, float cost, float priority, SearchNode parent, float2 position)
            {
                Cell = cell;
                Heading = heading;
                Cost = cost;
                Priority = priority;
                Parent = parent;
                Position = position;
            }

            public int2 Cell { get; }
            public int Heading { get; }
            public float Cost { get; }
            public float Priority { get; }
            public SearchNode Parent { get; }
            public float2 Position { get; }
        }

        private sealed class SearchHeap
        {
            private readonly List<SearchNode> m_Items = new List<SearchNode>();
            private readonly List<long> m_Order = new List<long>();
            private long m_Sequence;

            public int Count
            {
                get { return m_Items.Count; }
            }

            public void Push(SearchNode node)
            {
                m_Items.Add(node);
                m_Order.Add(m_Sequence++);
                SiftUp(m_Items.Count - 1);
            }

            public SearchNode Pop()
            {
                SearchNode top = m_Items[0];
                int last = m_Items.Count - 1;
                m_Items[0] = m_Items[last];
                m_Order[0] = m_Order[last];
                m_Items.RemoveAt(last);
                m_Order.RemoveAt(last);
                if (m_Items.Count > 0)
                {
                    SiftDown(0);
                }
                return top;
            }

            private bool Less(int a, int b)
            {
                if (m_Items[a].Priority != m_Items[b].Priority)
                {
                    return m_Items[a].Priority < m_Items[b].Priority;
                }
                return m_Order[a] < m_Order[b];
            }

            private void SiftUp(int index)
            {
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (!Less(index, parent))
                    {
                        break;
                    }
                    Swap(index, parent);
                    index = parent;
                }
            }

            private void SiftDown(int index)
            {
                while (true)
                {
                    int left = index * 2 + 1;
                    int right = left + 1;
                    int smallest = index;
                    if (left < m_Items.Count && Less(left, smallest))
                    {
                        smallest = left;
                    }
                    if (right < m_Items.Count && Less(right, smallest))
                    {
                        smallest = right;
                    }
                    if (smallest == index)
                    {
                        break;
                    }
                    Swap(index, smallest);
                    index = smallest;
                }
            }

            private void Swap(int a, int b)
            {
                SearchNode node = m_Items[a];
                m_Items[a] = m_Items[b];
                m_Items[b] = node;
                long order = m_Order[a];
                m_Order[a] = m_Order[b];
                m_Order[b] = order;
            }
        }

        private sealed class VerticalProfile
        {
            private readonly SketchRoad m_Road;
            private readonly INetworkSiteSampler m_Sampler;
            private readonly List<float2> m_Waypoints;
            private readonly float m_StartHeight;
            private readonly float m_EndHeight;
            private readonly float m_Total;

            public VerticalProfile(SketchRoad road, INetworkSiteSampler sampler, List<float2> waypoints)
            {
                m_Road = road;
                m_Sampler = sampler;
                m_Waypoints = waypoints;
                m_StartHeight = sampler.TerrainHeight(waypoints[0].x, waypoints[0].y) + road.E1;
                m_EndHeight = sampler.TerrainHeight(waypoints[waypoints.Count - 1].x, waypoints[waypoints.Count - 1].y) + road.E2;
                float total = 0f;
                for (int i = 0; i + 1 < waypoints.Count; i++)
                {
                    total += math.distance(waypoints[i], waypoints[i + 1]);
                }
                m_Total = math.max(total, 0.001f);
            }

            public float HeightAt(float2 point)
            {
                if (m_Road.Mode == RoadBuildMode.Ground)
                {
                    return m_Sampler.TerrainHeight(point.x, point.y);
                }
                float walked = 0f;
                float best = 0f;
                float bestDistance = float.MaxValue;
                for (int i = 0; i + 1 < m_Waypoints.Count; i++)
                {
                    float2 ab = m_Waypoints[i + 1] - m_Waypoints[i];
                    float lengthSq = math.lengthsq(ab);
                    float t = lengthSq > 0.0001f ? math.clamp(math.dot(point - m_Waypoints[i], ab) / lengthSq, 0f, 1f) : 0f;
                    float distance = math.distance(point, m_Waypoints[i] + ab * t);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = walked + math.sqrt(lengthSq) * t;
                    }
                    walked += math.sqrt(lengthSq);
                }
                return math.lerp(m_StartHeight, m_EndHeight, math.clamp(best / m_Total, 0f, 1f));
            }

            public RoadPath Lift(RoadPath flat)
            {
                return new RoadPath(
                    WithHeight(flat.A),
                    WithHeight(flat.B),
                    WithHeight(flat.C),
                    WithHeight(flat.D));
            }

            private float3 WithHeight(float3 point)
            {
                return new float3(point.x, HeightAt(point.xz), point.z);
            }
        }

        private static void ApplyClearanceBumps(
            NetworkSketch sketch,
            Dictionary<string, SketchRoad> roads,
            Dictionary<string, SolvedSketchRoad> solved,
            INetworkSiteSampler sampler,
            BlueprintPlanResult result)
        {
            foreach (SketchCrossing crossing in sketch.Crossings)
            {
                if (crossing.Kind == "at-grade")
                {
                    continue;
                }
                SolvedSketchRoad upper;
                SolvedSketchRoad lower;
                if (crossing.Upper == crossing.A)
                {
                    solved.TryGetValue(crossing.A, out upper);
                    solved.TryGetValue(crossing.B, out lower);
                }
                else
                {
                    solved.TryGetValue(crossing.B, out upper);
                    solved.TryGetValue(crossing.A, out lower);
                }
                if (upper == null || lower == null)
                {
                    continue;
                }
                float clearance = math.max(upper.Sketch.Clearance, lower.Sketch.Clearance);
                float2 site;
                if (!FindCenterCrossing(upper.Center, lower.Center, out site))
                {
                    continue;
                }
                foreach (ResolvedCourse course in upper.Courses)
                {
                    RaiseForClearance(course, lower, site, clearance, sampler, result, upper.Sketch);
                    if (result.Status == BlueprintStatus.Invalid)
                    {
                        return;
                    }
                }
            }
        }

        private static void RaiseForClearance(
            ResolvedCourse course,
            SolvedSketchRoad lower,
            float2 site,
            float clearance,
            INetworkSiteSampler sampler,
            BlueprintPlanResult result,
            SketchRoad sketch)
        {
            List<float2> center = SampleCenter(course.Path);
            float walked = 0f;
            float best = 0f;
            float bestDistance = float.MaxValue;
            for (int i = 0; i + 1 < center.Count; i++)
            {
                float2 ab = center[i + 1] - center[i];
                float lengthSq = math.lengthsq(ab);
                float t = lengthSq > 0.0001f ? math.clamp(math.dot(site - center[i], ab) / lengthSq, 0f, 1f) : 0f;
                float distance = math.distance(site, center[i] + ab * t);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = walked + math.sqrt(lengthSq) * t;
                }
                walked += math.sqrt(lengthSq);
            }
            float lowerHeight = HeightOnSolved(lower, site);
            float total = math.max(walked, 0.001f);
            float lift = (lowerHeight + clearance) - math.lerp(course.Path.A.y, course.Path.D.y, math.clamp(best / total, 0f, 1f));
            if (lift <= 0f)
            {
                return;
            }
            float3 a = course.Path.A;
            float3 b = course.Path.B;
            float3 c = course.Path.C;
            float3 d = course.Path.D;
            a.y += lift * BumpWeight(new float2(a.x, a.z), site);
            b.y += lift * BumpWeight(new float2(b.x, b.z), site);
            c.y += lift * BumpWeight(new float2(c.x, c.z), site);
            d.y += lift * BumpWeight(new float2(d.x, d.z), site);
            var raised = new RoadPath(a, b, c, d);
            if (!ValidateCourseGrades(sketch, sampler, raised, result))
            {
                return;
            }
            course.Path = raised;
            course.E1 = raised.A.y - sampler.TerrainHeight(raised.A.x, raised.A.z);
            course.E2 = raised.D.y - sampler.TerrainHeight(raised.D.x, raised.D.z);
        }

        private static float BumpWeight(float2 point, float2 site)
        {
            float distance = math.distance(point, site);
            if (distance >= kClearanceBumpHalfLength)
            {
                return 0f;
            }
            float t = 1f - distance / kClearanceBumpHalfLength;
            return t * t * (3f - 2f * t);
        }

        private static float HeightOnSolved(SolvedSketchRoad road, float2 site)
        {
            float bestHeight = 0f;
            float bestDistance = float.MaxValue;
            foreach (ResolvedCourse course in road.Courses)
            {
                List<float2> center = SampleCenter(course.Path);
                for (int i = 0; i < center.Count; i++)
                {
                    float step = i / (float)math.max(center.Count - 1, 1);
                    float distance = math.distance(site, center[i]);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestHeight = CubicHeight(course.Path, step);
                    }
                }
            }
            return bestHeight;
        }

        internal static bool ValidateCourse(
            NetworkSketch sketch,
            SketchRoad road,
            INetworkSiteSampler sampler,
            RoadPath path,
            BlueprintPlanResult result)
        {
            float approx = math.distance(path.A, path.B)
                + math.distance(path.B, path.C) + math.distance(path.C, path.D);
            int steps = math.max(2, (int)math.ceil(approx / kFitSample) + 1);
            float3 previous = default;
            for (int i = 0; i < steps; i++)
            {
                float t = steps == 1 ? 0f : i / (float)(steps - 1);
                float3 center = CubicPoint3(path, t);
                float2 tangent = CubicTangent(path, t);
                float2 normal = math.normalizesafe(new float2(-tangent.y, tangent.x), new float2(1f, 0f));
                for (int side = -1; side <= 1; side++)
                {
                    float3 probe = center;
                    probe.xz += normal * (side * road.HalfWidth);
                    if (!sampler.Owned(probe.x, probe.z))
                    {
                        AddDiagnostic(result, road.Id, "ownership", "route leaves owned land", probe.x, probe.z, 0f, 1f);
                        return false;
                    }
                    if (road.Mode == RoadBuildMode.Ground
                        && sampler.WaterDepth(probe.x, probe.z) >= kWaterDepthBlock)
                    {
                        AddDiagnostic(result, road.Id, "water", "ground route crosses water; use grade-separated with an explicit bridge", probe.x, probe.z, sampler.WaterDepth(probe.x, probe.z), kWaterDepthBlock);
                        return false;
                    }
                    foreach (SketchBlockedRect blocked in sketch.Blocked)
                    {
                        if (probe.x >= blocked.MinX && probe.x <= blocked.MaxX
                            && probe.z >= blocked.MinZ && probe.z <= blocked.MaxZ)
                        {
                            AddDiagnostic(result, road.Id, "blocked", "route enters a keep-out area", probe.x, probe.z, 0f, 0f);
                            return false;
                        }
                    }
                }
                if (i > 0)
                {
                    float horizontal = math.distance(previous.xz, center.xz);
                    float vertical = math.abs(center.y - previous.y);
                    float grade = horizontal > 0.001f ? vertical / horizontal : (vertical > 0.001f ? float.PositiveInfinity : 0f);
                    if (grade > road.MaxGrade + 0.000001f)
                    {
                        AddDiagnostic(result, road.Id, "grade", "grade exceeds the allowed maximum", center.x, center.z, grade, road.MaxGrade);
                        return false;
                    }
                }
                previous = center;
            }
            if (!ValidateStructures(road, sampler, path, result))
            {
                return false;
            }
            if (road.MinCurveRadius > 0f && !ValidateCurvature(road, path, result))
            {
                return false;
            }
            return true;
        }

        private static bool ValidateCourseGrades(
            SketchRoad road,
            INetworkSiteSampler sampler,
            RoadPath path,
            BlueprintPlanResult result)
        {
            float approx = math.distance(path.A, path.B)
                + math.distance(path.B, path.C) + math.distance(path.C, path.D);
            int steps = math.max(2, (int)math.ceil(approx / kFitSample) + 1);
            float3 previous = CubicPoint3(path, 0f);
            for (int i = 1; i < steps; i++)
            {
                float3 center = CubicPoint3(path, i / (float)(steps - 1));
                float horizontal = math.distance(previous.xz, center.xz);
                float vertical = math.abs(center.y - previous.y);
                float grade = horizontal > 0.001f ? vertical / horizontal : (vertical > 0.001f ? float.PositiveInfinity : 0f);
                if (grade > road.MaxGrade + 0.000001f)
                {
                    AddDiagnostic(result, road.Id, "grade", "clearance lift pushes the grade past the allowed maximum", center.x, center.z, grade, road.MaxGrade);
                    return false;
                }
                previous = center;
            }
            return true;
        }

        private static bool ValidateStructures(SketchRoad road, INetworkSiteSampler sampler, RoadPath path, BlueprintPlanResult result)
        {
            bool buried = false;
            bool elevated = false;
            bool wet = false;
            int steps = 24;
            for (int i = 0; i <= steps; i++)
            {
                float3 point = CubicPoint3(path, i / (float)steps);
                float terrain = sampler.TerrainHeight(point.x, point.z);
                if (point.y < terrain - 1.5f)
                {
                    buried = true;
                }
                if (point.y > terrain + 2.5f)
                {
                    elevated = true;
                }
                if (sampler.WaterDepth(point.x, point.z) >= kWaterDepthBlock && point.y <= terrain + 1f)
                {
                    wet = true;
                }
            }
            if (road.Mode == RoadBuildMode.Ground)
            {
                return true;
            }
            if (wet && !Allows(road, "bridge"))
            {
                AddDiagnostic(result, road.Id, "structure", "water crossing needs an explicit bridge allowance", path.A.x, path.A.z, 0f, 0f);
                return false;
            }
            if (buried && !Allows(road, "tunnel"))
            {
                AddDiagnostic(result, road.Id, "structure", "buried section needs an explicit tunnel allowance", path.A.x, path.A.z, 0f, 0f);
                return false;
            }
            if (elevated && !Allows(road, "bridge") && !Allows(road, "elevated"))
            {
                AddDiagnostic(result, road.Id, "structure", "elevated section needs an explicit bridge or elevated allowance", path.A.x, path.A.z, 0f, 0f);
                return false;
            }
            return true;
        }

        private static bool Allows(SketchRoad road, string structure)
        {
            foreach (string entry in road.Structures)
            {
                if (string.Equals(entry, structure, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ValidateCurvature(SketchRoad road, RoadPath path, BlueprintPlanResult result)
        {
            int steps = 32;
            float3 previous = CubicPoint3(path, 0f);
            float2 previousTangent = CubicTangent(path, 0f);
            for (int i = 1; i <= steps; i++)
            {
                float3 point = CubicPoint3(path, i / (float)steps);
                float2 tangent = CubicTangent(path, i / (float)steps);
                float segment = math.distance(previous.xz, point.xz);
                float turn = math.acos(math.clamp(math.dot(previousTangent, tangent), -1f, 1f));
                if (segment > 0.01f && turn > 0.0001f)
                {
                    float radius = segment / turn;
                    if (radius < road.MinCurveRadius)
                    {
                        AddDiagnostic(result, road.Id, "curvature", "curve is tighter than the allowed radius", point.x, point.z, radius, road.MinCurveRadius);
                        return false;
                    }
                }
                previous = point;
                previousTangent = tangent;
            }
            return true;
        }

        private static bool CheckCrossings(
            NetworkSketch sketch,
            Dictionary<string, SolvedSketchRoad> solved,
            BlueprintPlanResult result)
        {
            var ids = new List<string>(solved.Keys);
            ids.Sort(StringComparer.Ordinal);
            for (int i = 0; i < ids.Count; i++)
            {
                for (int j = i + 1; j < ids.Count; j++)
                {
                    if (!CheckPair(sketch, solved[ids[i]], solved[ids[j]], result))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool CheckPair(
            NetworkSketch sketch,
            SolvedSketchRoad first,
            SolvedSketchRoad second,
            BlueprintPlanResult result)
        {
            float2 site;
            if (!FindCenterCrossing(first.Center, second.Center, out site))
            {
                return true;
            }
            if (JoinedBySketchAnchor(sketch, first, second, site))
            {
                return true;
            }
            if (SharesAnchor(sketch, first.Sketch, second.Sketch))
            {
                float firstHeight = HeightOnSolved(first, site);
                float secondHeight = HeightOnSolved(second, site);
                if (math.abs(firstHeight - secondHeight) > 1f)
                {
                    AddDiagnostic(result, first.Sketch.Id, "connection",
                        "shared junction has mismatched heights; declare an over/under crossing instead",
                        site.x, site.y, math.abs(firstHeight - secondHeight), 1f);
                    return false;
                }
                return true;
            }
            SketchCrossing crossing = FindCrossing(sketch, first.Sketch.Id, second.Sketch.Id);
            if (crossing == null)
            {
                AddDiagnostic(result, first.Sketch.Id, "crossing",
                    "roads cross without a shared junction or a declared crossing; connect them or declare over/under",
                    site.x, site.y, 0f, 0f);
                return false;
            }
            if (crossing.Kind == "at-grade")
            {
                AddDiagnostic(result, first.Sketch.Id, "crossing",
                    "at-grade crossing needs a shared junction; connect the roads or declare over/under",
                    site.x, site.y, 0f, 0f);
                return false;
            }
            float upperHeight;
            float lowerHeight;
            if (crossing.Upper == first.Sketch.Id)
            {
                upperHeight = HeightOnSolved(first, site);
                lowerHeight = HeightOnSolved(second, site);
            }
            else if (crossing.Upper == second.Sketch.Id)
            {
                upperHeight = HeightOnSolved(second, site);
                lowerHeight = HeightOnSolved(first, site);
            }
            else
            {
                AddDiagnostic(result, first.Sketch.Id, "crossing",
                    "separated crossing must name which road passes over",
                    site.x, site.y, 0f, 0f);
                return false;
            }
            float clearance = math.max(first.Sketch.Clearance, second.Sketch.Clearance);
            if (upperHeight - lowerHeight < clearance)
            {
                AddDiagnostic(result, first.Sketch.Id, "clearance",
                    "separated crossing is closer than the required clearance",
                    site.x, site.y, upperHeight - lowerHeight, clearance);
                return false;
            }
            return true;
        }

        private static bool JoinedBySketchAnchor(
            NetworkSketch sketch, SolvedSketchRoad first, SolvedSketchRoad second, float2 site)
        {
            var anchors = new Dictionary<string, SketchAnchor>(StringComparer.Ordinal);
            foreach (SketchAnchor anchor in sketch.Anchors)
            {
                anchors[anchor.Id] = anchor;
            }
            return EndJoins(first.Sketch, second.Sketch.Id, anchors, site, first.Center)
                || EndJoins(second.Sketch, first.Sketch.Id, anchors, site, second.Center);
        }

        private static bool EndJoins(
            SketchRoad road, string otherId, Dictionary<string, SketchAnchor> anchors, float2 site, List<float2> center)
        {
            foreach (string endpoint in new[] { road.From, road.To })
            {
                SketchAnchor anchor;
                if (!anchors.TryGetValue(endpoint, out anchor) || anchor.Kind != SketchAnchorKind.SketchRoad)
                {
                    continue;
                }
                if (anchor.RoadId != otherId)
                {
                    continue;
                }
                float2 end = endpoint == road.From ? center[0] : center[center.Count - 1];
                if (math.distance(end, site) <= 12f)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool SharesAnchor(NetworkSketch sketch, SketchRoad first, SketchRoad second)
        {
            var anchors = new HashSet<string>(StringComparer.Ordinal);
            anchors.Add(first.From);
            anchors.Add(first.To);
            return anchors.Contains(second.From) || anchors.Contains(second.To);
        }

        private static SketchCrossing FindCrossing(NetworkSketch sketch, string a, string b)
        {
            foreach (SketchCrossing crossing in sketch.Crossings)
            {
                if ((crossing.A == a && crossing.B == b) || (crossing.A == b && crossing.B == a))
                {
                    return crossing;
                }
            }
            return null;
        }

        private static bool FindCenterCrossing(List<float2> first, List<float2> second, out float2 site)
        {
            site = default;
            for (int i = 0; i + 1 < first.Count; i++)
            {
                for (int j = 0; j + 1 < second.Count; j++)
                {
                    float2 hit;
                    if (SegmentsCross(first[i], first[i + 1], second[j], second[j + 1], out hit))
                    {
                        site = hit;
                        return true;
                    }
                }
            }
            return false;
        }

        internal static bool SegmentsCross(float2 a, float2 b, float2 c, float2 d, out float2 site)
        {
            site = default;
            float2 ab = b - a;
            float2 cd = d - c;
            float denom = ab.x * cd.y - ab.y * cd.x;
            if (math.abs(denom) < 0.000001f)
            {
                return false;
            }
            float2 ac = c - a;
            float t = (ac.x * cd.y - ac.y * cd.x) / denom;
            float u = (ac.x * ab.y - ac.y * ab.x) / denom;
            if (t < -0.01f || t > 1.01f || u < -0.01f || u > 1.01f)
            {
                return false;
            }
            site = a + ab * math.clamp(t, 0f, 1f);
            return true;
        }

        internal static float3 CubicPoint3(RoadPath path, float t)
        {
            float inverse = 1f - t;
            float a = inverse * inverse * inverse;
            float b = 3f * inverse * inverse * t;
            float c = 3f * inverse * t * t;
            float d = t * t * t;
            return new float3(
                path.A.x * a + path.B.x * b + path.C.x * c + path.D.x * d,
                path.A.y * a + path.B.y * b + path.C.y * c + path.D.y * d,
                path.A.z * a + path.B.z * b + path.C.z * c + path.D.z * d);
        }

        internal static float2 CubicTangent(RoadPath path, float t)
        {
            float inverse = 1f - t;
            float2 tangent = 3f * inverse * inverse * (path.B.xz - path.A.xz)
                + 6f * inverse * t * (path.C.xz - path.B.xz)
                + 3f * t * t * (path.D.xz - path.C.xz);
            if (math.lengthsq(tangent) < 0.000001f)
            {
                tangent = path.D.xz - path.A.xz;
            }
            return math.normalize(tangent);
        }

        internal static float CourseLength(RoadPath path)
        {
            float length = 0f;
            float3 previous = CubicPoint3(path, 0f);
            for (int i = 1; i <= 32; i++)
            {
                float3 point = CubicPoint3(path, i / 32f);
                length += math.distance(previous, point);
                previous = point;
            }
            return length;
        }

        private static void AddDiagnostic(BlueprintPlanResult result, string road, string type, string message)
        {
            var diagnostic = new BlueprintDiagnostic
            {
                Road = road,
                Type = type,
                Message = message,
            };
            result.Diagnostics.Add(diagnostic);
        }

        internal static void AddDiagnostic(
            BlueprintPlanResult result, string road, string type, string message,
            float atX, float atZ, float observed, float allowed)
        {
            var diagnostic = new BlueprintDiagnostic
            {
                Road = road,
                Type = type,
                Message = message,
                AtX = atX,
                AtZ = atZ,
                HasAt = true,
                Observed = observed,
                Allowed = allowed,
            };
            result.Diagnostics.Add(diagnostic);
        }
    }
}
