using Unity.Mathematics;
using Xunit;

namespace CS2MCP
{
    public sealed class CurbSnapTests
    {
        [Fact]
        public void Request_on_the_left_snaps_to_that_curb_facing_out()
        {
            bool found = PlacementSearchMath.TrySnapCurb(
                new float2(-20f, 50f),
                30f,
                new[] { StraightRoad(-8f) },
                new[] { StraightRoad(8f) },
                out CurbSnap snap);

            Assert.True(found);
            Assert.Equal(0, snap.EdgeIndex);
            Assert.Equal(-8f, snap.Position.x, 3);
            Assert.Equal(50f, snap.Position.z, 3);
            Assert.Equal(-90f, snap.RotationDegrees, 3);
        }

        [Fact]
        public void Request_on_the_right_snaps_to_that_curb()
        {
            bool found = PlacementSearchMath.TrySnapCurb(
                new float2(20f, 10f),
                30f,
                new[] { StraightRoad(-8f) },
                new[] { StraightRoad(8f) },
                out CurbSnap snap);

            Assert.True(found);
            Assert.Equal(8f, snap.Position.x, 3);
            Assert.Equal(90f, snap.RotationDegrees, 3);
        }

        [Fact]
        public void Nearer_edge_wins()
        {
            bool found = PlacementSearchMath.TrySnapCurb(
                new float2(0f, 0f),
                40f,
                new[] { StraightRoad(-30f), StraightRoad(-4f) },
                new[] { StraightRoad(-14f), StraightRoad(4f) },
                out CurbSnap snap);

            Assert.True(found);
            Assert.Equal(1, snap.EdgeIndex);
        }

        [Fact]
        public void Outside_the_radius_finds_nothing()
        {
            bool found = PlacementSearchMath.TrySnapCurb(
                new float2(100f, 50f),
                10f,
                new[] { StraightRoad(-8f) },
                new[] { StraightRoad(8f) },
                out _);

            Assert.False(found);
        }

        private static CurbSide StraightRoad(float x)
        {
            float outward = math.sign(x);
            return new CurbSide(
                new[]
                {
                    new float3(x, 0f, 0f),
                    new float3(x, 0f, 100f),
                },
                new[]
                {
                    new float3(outward, 0f, 0f),
                    new float3(outward, 0f, 0f),
                });
        }
    }
}
