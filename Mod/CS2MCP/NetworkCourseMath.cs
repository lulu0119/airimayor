using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace CS2MCP
{
    internal enum NetworkCourseShape : byte
    {
        Straight,
        Simple,
        Complex,
        Parallel,
    }

    internal enum NetworkElevationProfile : byte
    {
        Linear,
        Ease,
        Arch,
    }

    internal enum NetworkJoinKind : byte
    {
        New,
        Node,
        Split,
    }

    internal readonly struct CourseSnapCandidate
    {
        public CourseSnapCandidate(int id, bool node, float distance)
        {
            Id = id;
            Node = node;
            Distance = distance;
        }

        public int Id { get; }
        public bool Node { get; }
        public float Distance { get; }
    }

    /// <summary>
    /// Pure course geometry for build_network. The handler supplies terrain,
    /// anchors and an existing path; this module only shapes beziers.
    /// </summary>
    internal static class NetworkCourseMath
    {
        public static float DefaultReach(float2 startDirection, float2 endHandleDirection)
        {
            float aligned = math.dot(
                math.normalizesafe(startDirection),
                -math.normalizesafe(endHandleDirection));
            return math.lerp(0.75f, 0.33f, math.saturate((aligned + 1f) / 2f));
        }

        /// <summary>
        /// Picks the edge tangent sense that faces <paramref name="desired"/>.
        /// Each entry points from the node into that edge. No edges returns
        /// <paramref name="desired"/> itself.
        /// </summary>
        public static float2 TangentToward(float2 desired, ReadOnlySpan<float2> intoEdge)
        {
            float2 fallback = math.normalizesafe(desired);
            if (intoEdge.Length == 0 || math.lengthsq(fallback) < 1e-8f)
            {
                return fallback;
            }

            float best = float.NegativeInfinity;
            float2 chosen = fallback;
            for (int i = 0; i < intoEdge.Length; i++)
            {
                float2 axis = math.normalizesafe(intoEdge[i]);
                if (math.lengthsq(axis) < 1e-8f)
                {
                    continue;
                }
                Consider(axis, fallback, ref best, ref chosen);
                Consider(-axis, fallback, ref best, ref chosen);
            }
            return chosen;
        }

        public static bool TryPickSnap(
            IReadOnlyList<CourseSnapCandidate> candidates,
            float radius,
            out CourseSnapCandidate picked)
        {
            picked = default;
            bool found = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                CourseSnapCandidate candidate = candidates[i];
                if (candidate.Distance > radius)
                {
                    continue;
                }
                if (!found
                    || candidate.Distance < picked.Distance
                    || (candidate.Distance == picked.Distance && candidate.Node && !picked.Node))
                {
                    picked = candidate;
                    found = true;
                }
            }
            return found;
        }

        public static RoadPath Simple(
            float3 start,
            float2 startDirection,
            float3 end,
            float2 endHandleDirection,
            float departure,
            float arrival)
        {
            float length = math.distance(start.xz, end.xz);
            float3 b = start + new float3(startDirection.x, 0f, startDirection.y) * (length * departure);
            float3 c = end + new float3(endHandleDirection.x, 0f, endHandleDirection.y) * (length * arrival);
            b.y = math.lerp(start.y, end.y, 1f / 3f);
            c.y = math.lerp(start.y, end.y, 2f / 3f);
            return new RoadPath(start, b, c, end);
        }

        public static void Complex(
            float3 start,
            float2 startDirection,
            float3 end,
            float2 endHandleDirection,
            float departure,
            float arrival,
            bool hasMidpoint,
            float midX,
            float midZ,
            out RoadPath first,
            out RoadPath second)
        {
            RoadPath guide = Simple(start, startDirection, end, endHandleDirection, departure, arrival);
            float3 mid = hasMidpoint
                ? new float3(midX, math.lerp(start.y, end.y, 0.5f), midZ)
                : Point(guide, 0.5f);
            float2 approach = math.normalizesafe(mid.xz - start.xz);
            float2 depart = math.normalizesafe(end.xz - mid.xz);
            float2 midDirection = math.normalizesafe(approach + depart, approach);
            if (math.lengthsq(midDirection) < 1e-8f)
            {
                midDirection = math.normalizesafe(end.xz - start.xz, new float2(0f, 1f));
            }

            const float inner = 1f / 3f;
            float3 midDirection3 = new float3(midDirection.x, 0f, midDirection.y);
            float3 towardStart = mid - midDirection3 * (math.distance(mid.xz, start.xz) * inner);
            float3 towardEnd = mid + midDirection3 * (math.distance(mid.xz, end.xz) * inner);
            towardStart.y = math.lerp(start.y, mid.y, 2f / 3f);
            towardEnd.y = math.lerp(mid.y, end.y, 1f / 3f);
            float3 startControl = guide.B;
            startControl.y = math.lerp(start.y, mid.y, 1f / 3f);
            float3 endControl = guide.C;
            endControl.y = math.lerp(mid.y, end.y, 2f / 3f);
            first = new RoadPath(start, startControl, towardStart, mid);
            second = new RoadPath(mid, towardEnd, endControl, end);
        }

        public static RoadPath ApplyProfile(
            RoadPath path,
            float t0,
            float t1,
            float startElevation,
            float endElevation,
            NetworkElevationProfile profile,
            float easeStart,
            float easeEnd,
            float archHeight,
            float archAt)
        {
            return new RoadPath(
                Lift(path.A, ProfileHeight(t0, startElevation, endElevation, profile, easeStart, easeEnd, archHeight, archAt)),
                Lift(path.B, ProfileHeight(math.lerp(t0, t1, 1f / 3f), startElevation, endElevation, profile, easeStart, easeEnd, archHeight, archAt)),
                Lift(path.C, ProfileHeight(math.lerp(t0, t1, 2f / 3f), startElevation, endElevation, profile, easeStart, easeEnd, archHeight, archAt)),
                Lift(path.D, ProfileHeight(t1, startElevation, endElevation, profile, easeStart, easeEnd, archHeight, archAt)));
        }

        public static float ProfileHeight(
            float t,
            float startElevation,
            float endElevation,
            NetworkElevationProfile profile,
            float easeStart,
            float easeEnd,
            float archHeight,
            float archAt)
        {
            t = math.saturate(t);
            if (profile == NetworkElevationProfile.Ease)
            {
                float window = 1f - easeStart - easeEnd;
                float u;
                if (window <= 0.0001f)
                {
                    u = t < easeStart ? 0f : 1f;
                }
                else
                {
                    u = math.saturate((t - easeStart) / window);
                }
                float smooth = u * u * (3f - 2f * u);
                return math.lerp(startElevation, endElevation, smooth);
            }

            float linear = math.lerp(startElevation, endElevation, t);
            if (profile != NetworkElevationProfile.Arch)
            {
                return linear;
            }

            float side;
            if (t <= archAt)
            {
                side = archAt <= 0.0001f ? 1f : t / archAt;
            }
            else
            {
                side = (1f - archAt) <= 0.0001f ? 1f : (1f - t) / (1f - archAt);
            }
            return linear + archHeight * math.sin(side * math.PI * 0.5f);
        }

        public static float Length(RoadPath path)
        {
            const int steps = 16;
            float length = 0f;
            float3 previous = path.A;
            for (int i = 1; i <= steps; i++)
            {
                float3 point = Point(path, i / (float)steps);
                length += math.distance(previous, point);
                previous = point;
            }
            return length;
        }

        public static float SteepestGrade(RoadPath path)
        {
            const int steps = 16;
            float steepest = 0f;
            float3 previous = path.A;
            for (int i = 1; i <= steps; i++)
            {
                float3 point = Point(path, i / (float)steps);
                float horizontal = math.distance(previous.xz, point.xz);
                float vertical = math.abs(point.y - previous.y);
                float grade = horizontal > 0.001f
                    ? vertical / horizontal
                    : (vertical > 0.001f ? float.PositiveInfinity : 0f);
                steepest = math.max(steepest, grade);
                previous = point;
            }
            return steepest;
        }

        /// <summary>
        /// Moves a point to the right of the path. <paramref name="incoming"/> and
        /// <paramref name="outgoing"/> are horizontal travel directions; the first
        /// or last point of a path passes a zero vector for the missing side.
        /// </summary>
        public static float3 OffsetPoint(
            float3 position,
            float2 incoming,
            float2 outgoing,
            float offset,
            float raise)
        {
            float2 outDirection = math.normalizesafe(outgoing);
            float2 inDirection = math.normalizesafe(incoming, outDirection);
            if (math.lengthsq(outDirection) < 1e-8f)
            {
                outDirection = inDirection;
            }
            float2 inRight = new float2(inDirection.y, -inDirection.x);
            float2 outRight = new float2(outDirection.y, -outDirection.x);
            float2 normal = inRight + outRight;
            normal = math.lengthsq(normal) < 1e-6f
                ? outRight
                : math.normalize(normal);
            float denominator = math.dot(normal, outRight);
            float scale = math.abs(denominator) < 0.25f
                ? offset
                : offset / denominator;
            float limit = math.abs(offset) * 4f;
            scale = math.clamp(scale, -limit, limit);
            position.xz += normal * scale;
            position.y += raise;
            return position;
        }

        public static RoadPath JoinOffset(RoadPath oriented, float3 start, float3 end, float offset)
        {
            float2 startDirection = math.normalizesafe(oriented.B.xz - oriented.A.xz, end.xz - start.xz);
            float2 endDirection = math.normalizesafe(oriented.D.xz - oriented.C.xz, end.xz - start.xz);
            float2 startRight = new float2(startDirection.y, -startDirection.x);
            float2 endRight = new float2(endDirection.y, -endDirection.x);
            float3 simpleStart = oriented.A;
            simpleStart.xz += startRight * offset;
            float3 simpleEnd = oriented.D;
            simpleEnd.xz += endRight * offset;
            float3 b = oriented.B;
            b.xz += startRight * offset;
            b += start - simpleStart;
            float3 c = oriented.C;
            c.xz += endRight * offset;
            c += end - simpleEnd;
            float ratio = RescaleRatio(oriented, start, b, c, end);
            b = start + (b - start) * ratio;
            c = end + (c - end) * ratio;
            return new RoadPath(start, b, c, end);
        }

        /// <summary>
        /// Handle scale that keeps an offset bend as round as its source:
        /// the ratio of the offset length to the source length.
        /// </summary>
        private static float RescaleRatio(RoadPath oriented, float3 start, float3 b, float3 c, float3 end)
        {
            float source = Length(oriented);
            if (source < 0.001f)
            {
                return 1f;
            }
            return Length(new RoadPath(start, b, c, end)) / source;
        }

        public static float3 Point(RoadPath path, float t)
        {
            float inverse = 1f - t;
            float inverseSquared = inverse * inverse;
            float tSquared = t * t;
            return path.A * (inverseSquared * inverse)
                + path.B * (3f * inverseSquared * t)
                + path.C * (3f * inverse * tSquared)
                + path.D * (tSquared * t);
        }

        private static void Consider(float2 sense, float2 desired, ref float best, ref float2 chosen)
        {
            float score = math.dot(sense, desired);
            if (score > best)
            {
                best = score;
                chosen = sense;
            }
        }

        private static float3 Lift(float3 point, float elevation)
        {
            point.y += elevation;
            return point;
        }
    }
}
