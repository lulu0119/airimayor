using Unity.Mathematics;
using Xunit;

namespace CS2MCP
{
    public sealed class NetworkCourseMathTests
    {
        [Fact]
        public void Snap_picks_the_closer_hit_and_prefers_a_node_on_a_tie()
        {
            var candidates = new[]
            {
                new CourseSnapCandidate(0, false, 4f),
                new CourseSnapCandidate(1, true, 6f),
                new CourseSnapCandidate(2, true, 4f),
            };

            Assert.True(NetworkCourseMath.TryPickSnap(candidates, 8f, out CourseSnapCandidate picked));
            Assert.Equal(2, picked.Id);

            Assert.False(NetworkCourseMath.TryPickSnap(candidates, 3f, out _));
        }

        [Fact]
        public void Default_reach_is_shorter_when_the_tangents_continue()
        {
            float2 forward = new float2(1f, 0f);
            Assert.Equal(0.33f, NetworkCourseMath.DefaultReach(forward, -forward), 3);
            Assert.Equal(0.75f, NetworkCourseMath.DefaultReach(forward, forward), 3);
        }

        [Fact]
        public void Tangent_toward_uses_the_sense_facing_the_chord()
        {
            float2[] intoEdge = { new float2(1f, 0f) };

            float2 facingBack = NetworkCourseMath.TangentToward(new float2(-1f, 0f), intoEdge);

            Assert.Equal(-1f, facingBack.x, 3);
            Assert.Equal(0f, facingBack.y, 3);
        }

        [Fact]
        public void Simple_curve_with_the_chord_as_tangent_stays_in_line()
        {
            float3 start = new float3(0f, 0f, 0f);
            float3 end = new float3(0f, 10f, 90f);
            float2 chord = math.normalizesafe(end.xz - start.xz);
            float reach = NetworkCourseMath.DefaultReach(chord, -chord);
            RoadPath path = NetworkCourseMath.Simple(start, chord, end, -chord, reach, reach);

            Assert.Equal(0f, path.B.x, 3);
            Assert.Equal(0f, path.C.x, 3);
            float3 middle = NetworkCourseMath.Point(path, 0.5f);
            Assert.Equal(0f, middle.x, 3);
            Assert.Equal(5f, middle.y, 3);
        }

        [Fact]
        public void Complex_midpoint_is_the_joint_of_both_edges()
        {
            float3 start = new float3(0f, 0f, 0f);
            float3 end = new float3(100f, 0f, 0f);
            NetworkCourseMath.Complex(
                start,
                new float2(1f, 0f),
                end,
                new float2(-1f, 0f),
                0.5f,
                0.5f,
                true,
                40f,
                30f,
                out RoadPath first,
                out RoadPath second);

            Assert.Equal(40f, first.D.x, 3);
            Assert.Equal(30f, first.D.z, 3);
            Assert.Equal(first.D, second.A);
            float3 intoJoint = first.D - first.C;
            float3 outOfJoint = second.B - second.A;
            float cross = intoJoint.x * outOfJoint.z - intoJoint.z * outOfJoint.x;
            Assert.Equal(0f, cross, 3);
        }

        [Fact]
        public void Ease_stays_flat_at_each_end_and_arch_crests_at_archAt()
        {
            Assert.Equal(0f, Height(0.1f, NetworkElevationProfile.Ease, 0f, 10f, 0.1f, 0.1f, 0f, 0.5f), 3);
            Assert.Equal(10f, Height(0.9f, NetworkElevationProfile.Ease, 0f, 10f, 0.1f, 0.1f, 0f, 0.5f), 3);
            Assert.Equal(5f, Height(0.5f, NetworkElevationProfile.Ease, 0f, 10f, 0.1f, 0.1f, 0f, 0.5f), 3);

            Assert.Equal(0f, Height(0f, NetworkElevationProfile.Arch, 0f, 0f, 0f, 0f, 12f, 0.5f), 3);
            Assert.Equal(12f, Height(0.5f, NetworkElevationProfile.Arch, 0f, 0f, 0f, 0f, 12f, 0.5f), 3);
            Assert.Equal(0f, Height(1f, NetworkElevationProfile.Arch, 0f, 0f, 0f, 0f, 12f, 0.5f), 3);
        }

        [Fact]
        public void Positive_offset_moves_a_northbound_road_east()
        {
            float3 start = new float3(0f, 2f, 0f);
            float3 end = new float3(0f, 2f, 100f);
            var oriented = RoadPath.Straight(start, end);
            float3 from = NetworkCourseMath.OffsetPoint(start, float2.zero, new float2(0f, 1f), 10f, 0f);
            float3 to = NetworkCourseMath.OffsetPoint(end, new float2(0f, 1f), float2.zero, 10f, 0f);
            RoadPath copy = NetworkCourseMath.JoinOffset(oriented, from, to, 10f);

            Assert.Equal(10f, copy.A.x, 3);
            Assert.Equal(10f, copy.D.x, 3);
            Assert.Equal(0f, copy.A.z, 3);
            Assert.Equal(100f, copy.D.z, 3);
            Assert.Equal(10f, NetworkCourseMath.Point(copy, 0.5f).x, 3);
        }

        [Fact]
        public void JoinOffset_scales_handles_by_the_offset_length_ratio_on_a_bend()
        {
            var oriented = new RoadPath(
                new float3(0f, 0f, 0f),
                new float3(50f, 0f, 0f),
                new float3(100f, 0f, 50f),
                new float3(100f, 0f, 100f));
            float3 start = NetworkCourseMath.OffsetPoint(
                oriented.A, float2.zero, new float2(1f, 0f), 8f, 0f);
            float3 end = NetworkCourseMath.OffsetPoint(
                oriented.D, new float2(0f, 1f), float2.zero, 8f, 0f);
            RoadPath copy = NetworkCourseMath.JoinOffset(oriented, start, end, 8f);

            float ratio = NetworkCourseMath.Length(copy) / NetworkCourseMath.Length(oriented);
            Assert.True(ratio > 1f);
            Assert.True(math.distance(copy.B, copy.A) > math.distance(oriented.B, oriented.A));
            Assert.True(math.distance(copy.D, copy.C) > math.distance(oriented.D, oriented.C));
            Assert.Equal(8f, math.distance(
                NetworkCourseMath.Point(oriented, 0.5f).xz,
                NetworkCourseMath.Point(copy, 0.5f).xz), 0);
        }

        private static float Height(
            float t,
            NetworkElevationProfile profile,
            float startElevation,
            float endElevation,
            float easeStart,
            float easeEnd,
            float archHeight,
            float archAt)
        {
            return NetworkCourseMath.ProfileHeight(
                t,
                startElevation,
                endElevation,
                profile,
                easeStart,
                easeEnd,
                archHeight,
                archAt);
        }
    }
}
