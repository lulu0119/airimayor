using System.Collections.Generic;
using Xunit;

namespace CS2MCP
{
    public sealed class NetworkBuildArgumentsTests
    {
        [Fact]
        public void Road_defaults_to_ground_without_elevation()
        {
            bool parsed = Parse(true, Query(), out NetworkBuildArguments arguments, out _);

            Assert.True(parsed);
            Assert.Equal(RoadBuildMode.Ground, arguments.RoadMode);
            Assert.Equal(NetworkCourseShape.Straight, arguments.Shape);
            Assert.False(arguments.HasElevation);
        }

        [Fact]
        public void Road_accepts_explicit_ground_mode()
        {
            bool parsed = Parse(
                true,
                Query(("mode", "ground")),
                out NetworkBuildArguments arguments,
                out _);

            Assert.True(parsed);
            Assert.Equal(RoadBuildMode.Ground, arguments.RoadMode);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("bridge")]
        public void Road_rejects_unknown_or_empty_mode(string mode)
        {
            bool parsed = Parse(true, Query(("mode", mode)), out _, out string error);

            Assert.False(parsed);
            Assert.Contains("ground", error);
            Assert.Contains("grade-separated", error);
        }

        [Fact]
        public void Utility_has_no_road_mode()
        {
            bool parsed = Parse(false, Query(), out NetworkBuildArguments arguments, out _);

            Assert.True(parsed);
            Assert.Null(arguments.RoadMode);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("ground")]
        [InlineData("grade-separated")]
        public void Utility_rejects_any_explicit_mode(string mode)
        {
            bool parsed = Parse(false, Query(("mode", mode)), out _, out string error);

            Assert.False(parsed);
            Assert.Contains("only valid for road prefabs", error);
        }

        [Theory]
        [InlineData("cx", "10")]
        [InlineData("cz", "10")]
        public void Removed_control_point_is_rejected(string key, string value)
        {
            bool parsed = Parse(true, Query((key, value)), out _, out string error);

            Assert.False(parsed);
            Assert.Contains("shape=simple", error);
            Assert.Contains("shape=complex", error);
        }

        [Fact]
        public void Straight_rejects_a_curve_field()
        {
            bool parsed = Parse(true, Query(("departure", "0.4")), out _, out string error);

            Assert.False(parsed);
            Assert.Contains("departure", error);
        }

        [Fact]
        public void Simple_preserves_departure_and_arrival()
        {
            bool parsed = Parse(
                true,
                Query(("shape", "simple"), ("departure", "0.4"), ("arrival", "0.2")),
                out NetworkBuildArguments arguments,
                out string error);

            Assert.True(parsed, error);
            Assert.Equal(NetworkCourseShape.Simple, arguments.Shape);
            Assert.Equal(0.4f, arguments.Departure);
            Assert.Equal(0.2f, arguments.Arrival);
        }

        [Fact]
        public void Complex_preserves_a_midpoint_and_rejects_one_coordinate()
        {
            bool parsed = Parse(
                true,
                Query(("shape", "complex"), ("mx", "12.5"), ("mz", "-7.25")),
                out NetworkBuildArguments arguments,
                out string error);

            Assert.True(parsed, error);
            Assert.True(arguments.HasMidpoint);
            Assert.Equal(12.5f, arguments.MidX);
            Assert.Equal(-7.25f, arguments.MidZ);

            bool half = Parse(true, Query(("shape", "complex"), ("mx", "1")), out _, out string halfError);
            Assert.False(half);
            Assert.Contains("both mx and mz", halfError);
        }

        [Fact]
        public void Parallel_requires_offset_and_rejects_mode_and_curve_fields()
        {
            bool missing = Parse(true, Query(("shape", "parallel")), out _, out string missingError);
            Assert.False(missing);
            Assert.Contains("offset", missingError);

            bool parsed = Parse(
                true,
                Query(("shape", "parallel"), ("offset", "20"), ("raise", "-4")),
                out NetworkBuildArguments arguments,
                out string error);
            Assert.True(parsed, error);
            Assert.Equal(20f, arguments.Offset);
            Assert.Equal(-4f, arguments.Raise);
            Assert.Null(arguments.RoadMode);

            bool mode = Parse(
                true,
                Query(("shape", "parallel"), ("offset", "20"), ("mode", "ground")),
                out _,
                out string modeError);
            Assert.False(mode);
            Assert.Contains("does not take mode", modeError);
        }

        [Fact]
        public void Utility_rejects_parallel()
        {
            bool parsed = Parse(false, Query(("shape", "parallel"), ("offset", "10")), out _, out string error);

            Assert.False(parsed);
            Assert.Contains("parallel copies a road", error);
        }

        [Fact]
        public void Ease_and_arch_require_grade_separated()
        {
            bool ground = Parse(true, Query(("profile", "ease")), out _, out string groundError);
            Assert.False(ground);
            Assert.Contains("grade-separated", groundError);

            bool ease = Parse(
                true,
                Query(
                    ("mode", "grade-separated"),
                    ("e1", "0"),
                    ("e2", "8"),
                    ("profile", "ease"),
                    ("easeStart", "0.2"),
                    ("easeEnd", "0.25")),
                out NetworkBuildArguments arguments,
                out string error);
            Assert.True(ease, error);
            Assert.Equal(NetworkElevationProfile.Ease, arguments.Profile);
            Assert.Equal(0.2f, arguments.EaseStart);
            Assert.Equal(0.25f, arguments.EaseEnd);

            bool arch = Parse(
                true,
                Query(
                    ("mode", "grade-separated"),
                    ("e1", "0"),
                    ("e2", "8"),
                    ("profile", "arch")),
                out _,
                out string archError);
            Assert.False(arch);
            Assert.Contains("archHeight", archError);
        }

        [Fact]
        public void Ground_road_rejects_elevation()
        {
            bool parsed = Parse(true, Query(("e1", "5")), out _, out string error);

            Assert.False(parsed);
            Assert.Contains("does not accept e1/e2", error);
        }

        [Theory]
        [InlineData("e1")]
        [InlineData("e2")]
        public void Grade_separated_road_requires_both_elevations(string provided)
        {
            bool parsed = Parse(
                true,
                Query(("mode", "grade-separated"), (provided, "8")),
                out _,
                out string error);

            Assert.False(parsed);
            Assert.Contains("requires both e1 and e2", error);
        }

        [Fact]
        public void Grade_separated_road_requires_a_nonzero_elevation()
        {
            bool parsed = Parse(
                true,
                Query(("mode", "grade-separated"), ("e1", "0"), ("e2", "0")),
                out _,
                out string error);

            Assert.False(parsed);
            Assert.Contains("nonzero elevation", error);
        }

        [Fact]
        public void Grade_separated_road_preserves_both_elevations()
        {
            bool parsed = Parse(
                true,
                Query(("mode", "grade-separated"), ("e1", "8"), ("e2", "12")),
                out NetworkBuildArguments arguments,
                out _);

            Assert.True(parsed);
            Assert.Equal(RoadBuildMode.GradeSeparated, arguments.RoadMode);
            Assert.True(arguments.HasElevation);
            Assert.Equal(8f, arguments.StartElevation);
            Assert.Equal(12f, arguments.EndElevation);
        }

        [Fact]
        public void Utility_preserves_one_explicit_elevation_for_legacy_behavior()
        {
            bool parsed = Parse(
                false,
                Query(("e1", "-15")),
                out NetworkBuildArguments arguments,
                out _);

            Assert.True(parsed);
            Assert.True(arguments.HasElevation);
            Assert.Equal(-15f, arguments.StartElevation);
            Assert.Equal(0f, arguments.EndElevation);
        }

        [Theory]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("61")]
        [InlineData("-31")]
        public void Elevation_must_be_finite_and_in_range(string elevation)
        {
            bool parsed = Parse(
                false,
                Query(("e1", elevation)),
                out _,
                out _);

            Assert.False(parsed);
        }

        private static bool Parse(
            bool isRoad,
            Dictionary<string, string> query,
            out NetworkBuildArguments arguments,
            out string error)
        {
            return NetworkBuildArguments.TryParse(
                query,
                isRoad,
                out arguments,
                out error);
        }

        private static Dictionary<string, string> Query(
            params (string Key, string Value)[] values)
        {
            var query = new Dictionary<string, string>();
            foreach ((string key, string value) in values)
            {
                query[key] = value;
            }
            return query;
        }
    }
}
