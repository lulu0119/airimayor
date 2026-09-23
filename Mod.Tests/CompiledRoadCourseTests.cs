using Unity.Entities;
using Unity.Mathematics;
using Xunit;

namespace CS2MCP
{
    public sealed class CompiledRoadCourseTests
    {
        [Fact]
        public void Elevated_s_curve_keeps_absolute_heights_and_both_connections()
        {
            var path = new RoadPath(new float3(0, 112, 0), new float3(30, 114, 50),
                new float3(70, 114, -50), new float3(100, 112, 0));
            var start = new RoadConnection(new Entity { Index = 101, Version = 2 });
            var end = new RoadConnection(new Entity { Index = 102, Version = 3 }, 0.4f);
            Assert.True(CompiledRoadCourse.TryCreate(path, RoadBuildMode.GradeSeparated, new float2(12),
                start, end, out CompiledRoadCourse course, out string error), error);
            Assert.Equal(path.A, course.Path.A);
            Assert.Equal(path.B, course.Path.B);
            Assert.Equal(path.C, course.Path.C);
            Assert.Equal(path.D, course.Path.D);
            Assert.Equal(start.Entity, course.Start.Entity);
            Assert.Equal(end.Entity, course.End.Entity);
            Assert.Equal(0.4f, course.End.Split);
        }

        [Fact]
        public void Tunnel_can_return_to_ground_at_one_endpoint()
        {
            Assert.True(Create(Path, RoadBuildMode.GradeSeparated, new float2(-10, 0)));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(-31, 0)]
        [InlineData(0, 61)]
        [InlineData(float.NaN, 4)]
        public void Invalid_structure_elevations_are_rejected(float start, float end)
        {
            Assert.False(Create(Path, RoadBuildMode.GradeSeparated, new float2(start, end)));
        }

        [Fact]
        public void Ground_cannot_silently_become_an_elevated_course()
        {
            Assert.False(Create(Path, RoadBuildMode.Ground, new float2(0, 10)));
            Assert.True(Create(Path, RoadBuildMode.Ground, default));
        }

        [Fact]
        public void Nonfinite_and_degenerate_curves_are_rejected()
        {
            Assert.False(Create(new RoadPath(Path.A, new float3(float.NaN), Path.C, Path.D),
                RoadBuildMode.Ground, default));
            Assert.False(Create(new RoadPath(Path.A, Path.A, Path.C, Path.D), RoadBuildMode.Ground, default));
            Assert.False(Create(new RoadPath(Path.A, Path.B, Path.D, Path.D), RoadBuildMode.Ground, default));
        }

        [Theory]
        [InlineData(-0.1f)]
        [InlineData(1.1f)]
        [InlineData(float.NaN)]
        public void Invalid_connection_parameters_are_rejected(float split)
        {
            Assert.False(CompiledRoadCourse.TryCreate(Path, RoadBuildMode.Ground, default,
                new RoadConnection(new Entity { Index = 1, Version = 1 }, split), default, out _, out _));
        }

        [Fact]
        public void Split_requires_an_entity()
        {
            Assert.False(CompiledRoadCourse.TryCreate(Path, RoadBuildMode.Ground, default,
                new RoadConnection(Entity.Null, 0.5f), default, out _, out _));
        }

        [Fact]
        public void Closed_curve_is_not_rejected_just_because_endpoints_match()
        {
            var loop = new RoadPath(new float3(0), new float3(100, 0, 50),
                new float3(100, 0, -50), new float3(0));
            Assert.True(Create(loop, RoadBuildMode.Ground, default));
        }

        private static RoadPath Path => RoadPath.Straight(new float3(0), new float3(100, 0, 0));

        private static bool Create(RoadPath path, RoadBuildMode mode, float2 elevations) =>
            CompiledRoadCourse.TryCreate(path, mode, elevations, default, default, out _, out _);
    }
}
