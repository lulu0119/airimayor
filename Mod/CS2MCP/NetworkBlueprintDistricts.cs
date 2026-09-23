using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace CS2MCP
{
    /// <summary>
    /// Direction-guided local streets inside one district rect. The field sets
    /// the shape (grid, radial, circular, mixed); explicit skeleton roads own
    /// all interchange connectivity. Generated streets only join the skeleton
    /// roads named by the sketch and never invent highway access on their own.
    /// Ground streets only; dead ends need explicit sketch permission.
    /// </summary>
    internal static class NetworkBlueprintDistricts
    {
        private const float kTraceStep = 8f;
        private const float kSnapSkeleton = 12f;
        private const float kSnapGenerated = 6f;
        private const float kShortDanglingLength = 60f;
        private const float kWaterBlock = 0.2f;
        private const float kMaxStreetChunk = 1200f;

        public static bool Generate(
            NetworkSketch sketch,
            Dictionary<string, SolvedSketchRoad> solved,
            INetworkSiteSampler sampler,
            BlueprintPlanResult result)
        {
            var districtIds = new List<string>();
            foreach (SketchDistrict district in sketch.Districts)
            {
                districtIds.Add(district.Id);
            }
            districtIds.Sort(StringComparer.Ordinal);
            foreach (string districtId in districtIds)
            {
                SketchDistrict district = FindDistrict(sketch, districtId);
                if (!GenerateDistrict(sketch, solved, sampler, district, result))
                {
                    result.Status = BlueprintStatus.Invalid;
                    return false;
                }
            }
            return true;
        }

        private static SketchDistrict FindDistrict(NetworkSketch sketch, string id)
        {
            foreach (SketchDistrict district in sketch.Districts)
            {
                if (district.Id == id)
                {
                    return district;
                }
            }
            return null;
        }

        private sealed class StreetDraft
        {
            public List<float2> Line = new List<float2>();
            public bool StartOnSkeleton;
            public bool EndOnSkeleton;
            public string StartCourse;
            public float StartSplit;
            public string EndCourse;
            public float EndSplit;
            public StreetDraft StartStreet;
            public float StartStreetSplit;
            public StreetDraft EndStreet;
            public float EndStreetSplit;
        }

        private static bool GenerateDistrict(
            NetworkSketch sketch,
            Dictionary<string, SolvedSketchRoad> solved,
            INetworkSiteSampler sampler,
            SketchDistrict district,
            BlueprintPlanResult result)
        {
            var skeleton = new List<ResolvedCourse>();
            foreach (KeyValuePair<string, SolvedSketchRoad> pair in solved)
            {
                if (district.ConnectTo.Count > 0 && !district.ConnectTo.Contains(pair.Key))
                {
                    continue;
                }
                skeleton.AddRange(pair.Value.Courses);
            }
            skeleton.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.Ordinal));
            var drafts = TraceStreets(sketch, district, sampler, skeleton);
            PruneDrafts(drafts, district, skeleton);
            int exits = 0;
            foreach (StreetDraft draft in drafts)
            {
                if (draft.StartOnSkeleton)
                {
                    exits++;
                }
                if (draft.EndOnSkeleton)
                {
                    exits++;
                }
            }
            if (drafts.Count == 0 || exits < district.MinExits)
            {
                NetworkBlueprintPlanner.AddDiagnostic(result, district.Id, "exits",
                    "district keeps " + exits + " skeleton exits (need " + district.MinExits + "); widen spacing, allow dead ends, or name more skeleton roads",
                    (district.MinX + district.MaxX) * 0.5f, (district.MinZ + district.MaxZ) * 0.5f, exits, district.MinExits);
                return false;
            }
            drafts.Sort((a, b) =>
            {
                int order = a.Line[0].x.CompareTo(b.Line[0].x);
                return order != 0 ? order : a.Line[0].y.CompareTo(b.Line[0].y);
            });
            var courseIds = new List<string>();
            for (int i = 0; i < drafts.Count; i++)
            {
                courseIds.Add(district.Id + "-s" + i);
            }
            var idByDraft = new Dictionary<StreetDraft, string>();
            for (int i = 0; i < drafts.Count; i++)
            {
                idByDraft[drafts[i]] = courseIds[i];
            }
            for (int i = 0; i < drafts.Count; i++)
            {
                StreetDraft draft = drafts[i];
                List<RoadPath> shapes = FitStreet(draft.Line);
                if (shapes.Count == 0)
                {
                    continue;
                }
                var streetRoad = new SketchRoad
                {
                    Id = courseIds[i],
                    Prefab = district.StreetPrefab,
                    Mode = RoadBuildMode.Ground,
                    HalfWidth = district.StreetHalfWidth,
                    MaxGrade = district.StreetMaxGrade,
                };
                for (int chunk = 0; chunk < shapes.Count; chunk++)
                {
                    RoadPath lifted = LiftGround(shapes[chunk], sampler);
                    int checkpoint = result.Diagnostics.Count;
                    if (!NetworkBlueprintPlanner.ValidateCourse(sketch, streetRoad, sampler, lifted, result))
                    {
                        result.Diagnostics.RemoveRange(checkpoint, result.Diagnostics.Count - checkpoint);
                        return false;
                    }
                    string courseId = shapes.Count > 1 ? courseIds[i] + "#" + chunk : courseIds[i];
                    var course = new ResolvedCourse
                    {
                        Id = courseId,
                        RoadId = courseIds[i],
                        Prefab = district.StreetPrefab,
                        Mode = RoadBuildMode.Ground,
                        Path = lifted,
                        E1 = 0f,
                        E2 = 0f,
                        DistrictId = district.Id,
                        Length = NetworkBlueprintPlanner.CourseLength(lifted),
                    };
                    if (chunk == 0)
                    {
                        course.Start = DraftAnchor(draft, true, idByDraft, courseIds[i], i);
                    }
                    else
                    {
                        course.Start = new BlueprintAnchorRef(BlueprintAnchorKind.Blueprint, courseId: courseIds[i] + "#" + (chunk - 1), split: 1f);
                    }
                    if (chunk == shapes.Count - 1)
                    {
                        course.End = DraftAnchor(draft, false, idByDraft, courseIds[i], i);
                    }
                    else
                    {
                        course.End = new BlueprintAnchorRef(BlueprintAnchorKind.None);
                    }
                    result.Courses.Add(course);
                }
            }
            return true;
        }

        private static BlueprintAnchorRef DraftAnchor(
            StreetDraft draft, bool isStart, Dictionary<StreetDraft, string> idByDraft, string ownId, int ownIndex)
        {
            if (isStart && draft.StartOnSkeleton)
            {
                return new BlueprintAnchorRef(BlueprintAnchorKind.Blueprint, courseId: draft.StartCourse, split: draft.StartSplit);
            }
            if (!isStart && draft.EndOnSkeleton)
            {
                return new BlueprintAnchorRef(BlueprintAnchorKind.Blueprint, courseId: draft.EndCourse, split: draft.EndSplit);
            }
            StreetDraft street = isStart ? draft.StartStreet : draft.EndStreet;
            if (street != null)
            {
                string target;
                if (idByDraft.TryGetValue(street, out target) && string.Compare(target, ownId, StringComparison.Ordinal) < 0)
                {
                    float split = isStart ? draft.StartStreetSplit : draft.EndStreetSplit;
                    return new BlueprintAnchorRef(BlueprintAnchorKind.Blueprint, courseId: target, split: split);
                }
            }
            return new BlueprintAnchorRef(BlueprintAnchorKind.None);
        }

        private static List<StreetDraft> TraceStreets(
            NetworkSketch sketch,
            SketchDistrict district,
            INetworkSiteSampler sampler,
            List<ResolvedCourse> skeleton)
        {
            var drafts = new List<StreetDraft>();
            float hash = Hash01(district.Id);
            float step = district.Spacing;
            float diagonal = math.distance(new float2(district.MinX, district.MinZ), new float2(district.MaxX, district.MaxZ));
            float maxLen = diagonal * 0.75f;
            for (float gx = district.MinX + (hash % 1f) * step; gx <= district.MaxX && drafts.Count < district.MaxStreets; gx += step)
            {
                for (float gz = district.MinZ + ((hash * 7f) % 1f) * step; gz <= district.MaxZ && drafts.Count < district.MaxStreets; gz += step)
                {
                    var seed = new float2(gx, gz);
                    if (!InsideRect(seed, district, 0f) || !GroundAllowed(sketch, sampler, seed, district.StreetHalfWidth))
                    {
                        continue;
                    }
                    if (NearDraft(drafts, seed, step * 0.4f))
                    {
                        continue;
                    }
                    StreetDraft draft = TraceSeed(sketch, district, sampler, skeleton, drafts, seed, maxLen);
                    if (draft != null && draft.Line.Count >= 2 && PolyLength(draft.Line) >= 24f)
                    {
                        drafts.Add(draft);
                    }
                }
            }
            return drafts;
        }

        private static StreetDraft TraceSeed(
            NetworkSketch sketch,
            SketchDistrict district,
            INetworkSiteSampler sampler,
            List<ResolvedCourse> skeleton,
            List<StreetDraft> existing,
            float2 seed,
            float maxLen)
        {
            var forward = WalkSide(sketch, district, sampler, skeleton, existing, seed, 1f, maxLen);
            var backward = WalkSide(sketch, district, sampler, skeleton, existing, seed, -1f, maxLen);
            backward.Reverse();
            var line = new List<float2>(backward.Count + forward.Count);
            for (int i = 0; i < backward.Count; i++)
            {
                line.Add(backward[i]);
            }
            for (int i = 1; i < forward.Count; i++)
            {
                line.Add(forward[i]);
            }
            if (line.Count < 2)
            {
                return null;
            }
            return new StreetDraft { Line = line };
        }

        private static List<float2> WalkSide(
            NetworkSketch sketch,
            SketchDistrict district,
            INetworkSiteSampler sampler,
            List<ResolvedCourse> skeleton,
            List<StreetDraft> existing,
            float2 seed,
            float side,
            float maxLen)
        {
            var points = new List<float2>();
            points.Add(seed);
            float2 position = seed;
            float walked = 0f;
            while (walked < maxLen)
            {
                float2 direction = FieldDirection(district, position) * side;
                position += direction * kTraceStep;
                walked += kTraceStep;
                if (!InsideRect(position, district, 0f))
                {
                    break;
                }
                if (!GroundAllowed(sketch, sampler, position, district.StreetHalfWidth))
                {
                    break;
                }
                float2 snapped = position;
                string courseId;
                float split;
                if (NearestCoursePoint(skeleton, position, kSnapSkeleton, out snapped, out courseId, out split))
                {
                    points.Add(snapped);
                    break;
                }
                if (NearestDraftPoint(existing, position, kSnapGenerated, out snapped))
                {
                    points.Add(snapped);
                    break;
                }
                points.Add(position);
            }
            return points;
        }

        private static void PruneDrafts(List<StreetDraft> drafts, SketchDistrict district, List<ResolvedCourse> skeleton)
        {
            ResolveDraftEnds(drafts, skeleton);
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = drafts.Count - 1; i >= 0; i--)
                {
                    StreetDraft draft = drafts[i];
                    bool startFree = !draft.StartOnSkeleton && draft.StartStreet == null;
                    bool endFree = !draft.EndOnSkeleton && draft.EndStreet == null;
                    float length = PolyLength(draft.Line);
                    if (startFree && endFree && length < kShortDanglingLength)
                    {
                        drafts.RemoveAt(i);
                        changed = true;
                    }
                    else if (!district.AllowDeadEnds && (startFree || endFree))
                    {
                        drafts.RemoveAt(i);
                        changed = true;
                    }
                }
                if (changed)
                {
                    ResolveDraftEnds(drafts, skeleton);
                }
            }
        }

        private static void ResolveDraftEnds(List<StreetDraft> drafts, List<ResolvedCourse> skeleton)
        {
            foreach (StreetDraft draft in drafts)
            {
                ResolveDraftEnd(draft, true, drafts, skeleton);
                ResolveDraftEnd(draft, false, drafts, skeleton);
            }
        }

        private static void ResolveDraftEnd(StreetDraft draft, bool isStart, List<StreetDraft> drafts, List<ResolvedCourse> skeleton)
        {
            float2 end = isStart ? draft.Line[0] : draft.Line[draft.Line.Count - 1];
            float2 snapped;
            string courseId;
            float split;
            if (NearestCoursePoint(skeleton, end, kSnapSkeleton, out snapped, out courseId, out split))
            {
                if (isStart)
                {
                    draft.StartOnSkeleton = true;
                    draft.StartCourse = courseId;
                    draft.StartSplit = split;
                    draft.StartStreet = null;
                }
                else
                {
                    draft.EndOnSkeleton = true;
                    draft.EndCourse = courseId;
                    draft.EndSplit = split;
                    draft.EndStreet = null;
                }
                return;
            }
            StreetDraft neighbor;
            float streetSplit;
            if (NearestDraftLine(drafts, draft, end, kSnapGenerated, out neighbor, out streetSplit))
            {
                if (isStart)
                {
                    draft.StartOnSkeleton = false;
                    draft.StartStreet = neighbor;
                    draft.StartStreetSplit = streetSplit;
                }
                else
                {
                    draft.EndOnSkeleton = false;
                    draft.EndStreet = neighbor;
                    draft.EndStreetSplit = streetSplit;
                }
                return;
            }
            if (isStart)
            {
                draft.StartOnSkeleton = false;
                draft.StartStreet = null;
            }
            else
            {
                draft.EndOnSkeleton = false;
                draft.EndStreet = null;
            }
        }

        private static bool NearestDraftLine(
            List<StreetDraft> drafts, StreetDraft self, float2 point, float maxDistance,
            out StreetDraft neighbor, out float split)
        {
            neighbor = null;
            split = 0f;
            float best = maxDistance;
            foreach (StreetDraft draft in drafts)
            {
                if (draft == self)
                {
                    continue;
                }
                float walked = 0f;
                float total = PolyLength(draft.Line);
                for (int i = 0; i < draft.Line.Count; i++)
                {
                    if (i > 0)
                    {
                        walked += math.distance(draft.Line[i - 1], draft.Line[i]);
                    }
                    float distance = math.distance(point, draft.Line[i]);
                    if (distance < best)
                    {
                        best = distance;
                        neighbor = draft;
                        split = total > 0.001f ? walked / total : 0f;
                    }
                }
            }
            return neighbor != null;
        }

        internal static float2 FieldDirection(SketchDistrict district, float2 point)
        {
            float2 grid = new float2(math.cos(district.AngleDeg * math.PI / 180f), math.sin(district.AngleDeg * math.PI / 180f));
            if (district.Field == "grid")
            {
                return grid;
            }
            float2 radial = point - new float2(district.CenterX, district.CenterZ);
            if (math.lengthsq(radial) < 0.01f)
            {
                return grid;
            }
            radial = math.normalize(radial);
            if (district.Field == "radial")
            {
                return radial;
            }
            if (district.Field == "circular")
            {
                return new float2(-radial.y, radial.x);
            }
            float wobble = 0.6f * math.sin(point.x * 0.01f + point.y * 0.004f) * math.cos(point.y * 0.01f - point.x * 0.003f);
            float2 mixed = math.normalize(grid + new float2(-radial.y, radial.x) * wobble);
            return mixed;
        }

        private static bool InsideRect(float2 point, SketchDistrict district, float margin)
        {
            return point.x >= district.MinX - margin && point.x <= district.MaxX + margin
                && point.y >= district.MinZ - margin && point.y <= district.MaxZ + margin;
        }

        private static bool GroundAllowed(NetworkSketch sketch, INetworkSiteSampler sampler, float2 point, float halfWidth)
        {
            foreach (SketchBlockedRect blocked in sketch.Blocked)
            {
                if (point.x >= blocked.MinX - halfWidth && point.x <= blocked.MaxX + halfWidth
                    && point.y >= blocked.MinZ - halfWidth && point.y <= blocked.MaxZ + halfWidth)
                {
                    return false;
                }
            }
            if (!sampler.Owned(point.x, point.y))
            {
                return false;
            }
            if (sampler.WaterDepth(point.x, point.y) >= kWaterBlock)
            {
                return false;
            }
            return true;
        }

        private static bool NearestCoursePoint(
            List<ResolvedCourse> courses, float2 point, float maxDistance,
            out float2 snapped, out string courseId, out float split)
        {
            snapped = point;
            courseId = null;
            split = 0f;
            float best = maxDistance;
            foreach (ResolvedCourse course in courses)
            {
                List<float2> center = NetworkBlueprintPlanner.SampleCenter(course.Path);
                for (int i = 0; i < center.Count; i++)
                {
                    float distance = math.distance(point, center[i]);
                    if (distance < best)
                    {
                        best = distance;
                        snapped = center[i];
                        courseId = course.Id;
                        split = center.Count > 1 ? i / (float)(center.Count - 1) : 0f;
                    }
                }
            }
            return courseId != null;
        }

        private static bool NearestDraftPoint(List<StreetDraft> drafts, float2 point, float maxDistance, out float2 snapped)
        {
            snapped = point;
            float best = maxDistance;
            bool found = false;
            foreach (StreetDraft draft in drafts)
            {
                foreach (float2 vertex in draft.Line)
                {
                    float distance = math.distance(point, vertex);
                    if (distance < best)
                    {
                        best = distance;
                        snapped = vertex;
                        found = true;
                    }
                }
            }
            return found;
        }

        private static bool NearDraft(List<StreetDraft> drafts, float2 seed, float maxDistance)
        {
            float2 unused;
            return NearestDraftPoint(drafts, seed, maxDistance, out unused);
        }

        private static float PolyLength(List<float2> line)
        {
            float total = 0f;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                total += math.distance(line[i], line[i + 1]);
            }
            return total;
        }

        private static float Hash01(string text)
        {
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char c in text)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
                return (hash % 1000u) / 1000f;
            }
        }

        private static List<RoadPath> FitStreet(List<float2> line)
        {
            float total = PolyLength(line);
            int chunks = math.max(1, (int)math.ceil(total / kMaxStreetChunk));
            var paths = new List<RoadPath>(chunks);
            for (int chunk = 0; chunk < chunks; chunk++)
            {
                float2 p0 = PointAt(line, total * chunk / chunks);
                float2 p3 = PointAt(line, total * (chunk + 1) / chunks);
                float2 t0 = TangentAt(line, total * chunk / chunks, true);
                float2 t3 = TangentAt(line, total * (chunk + 1) / chunks, false);
                float chord = math.max(math.distance(p0, p3), 1f);
                float scale = math.clamp(chord / 3f, 10f, 500f);
                paths.Add(new RoadPath(
                    new float3(p0.x, 0f, p0.y),
                    new float3(p0.x + t0.x * scale, 0f, p0.y + t0.y * scale),
                    new float3(p3.x - t3.x * scale, 0f, p3.y - t3.y * scale),
                    new float3(p3.x, 0f, p3.y)));
            }
            return paths;
        }

        private static float2 PointAt(List<float2> line, float distance)
        {
            float walked = 0f;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                float step = math.distance(line[i], line[i + 1]);
                if (walked + step >= distance)
                {
                    float t = step > 0.0001f ? (distance - walked) / step : 0f;
                    return math.lerp(line[i], line[i + 1], math.clamp(t, 0f, 1f));
                }
                walked += step;
            }
            return line[line.Count - 1];
        }

        private static float2 TangentAt(List<float2> line, float distance, bool isStart)
        {
            float total = PolyLength(line);
            float ahead = isStart ? math.min(distance + 16f, total) : math.max(distance - 16f, 0f);
            float2 here = PointAt(line, distance);
            float2 other = PointAt(line, ahead);
            float2 tangent = isStart ? other - here : here - other;
            if (math.lengthsq(tangent) < 0.0001f)
            {
                tangent = line[line.Count - 1] - line[0];
            }
            if (math.lengthsq(tangent) < 0.0001f)
            {
                return new float2(1f, 0f);
            }
            return math.normalize(tangent);
        }

        private static RoadPath LiftGround(RoadPath flat, INetworkSiteSampler sampler)
        {
            return new RoadPath(
                WithGround(flat.A, sampler),
                WithGround(flat.B, sampler),
                WithGround(flat.C, sampler),
                WithGround(flat.D, sampler));
        }

        private static float3 WithGround(float3 point, INetworkSiteSampler sampler)
        {
            return new float3(point.x, sampler.TerrainHeight(point.x, point.z), point.z);
        }
    }
}
