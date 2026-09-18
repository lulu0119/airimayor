using Xunit;

namespace CS2MCP
{
    public sealed class RoadFactsMathTests
    {
        [Fact]
        public void Highway_group_wins()
        {
            Assert.Equal(
                RoadFactsMath.RoadClassHighway,
                RoadFactsMath.Classify("Highways", "Highway Ramp"));
        }

        [Fact]
        public void Medium_group_wins()
        {
            Assert.Equal(
                RoadFactsMath.RoadClassMedium,
                RoadFactsMath.Classify("Medium Roads", "Medium Road"));
        }

        [Fact]
        public void Small_group_maps_to_minor()
        {
            Assert.Equal(
                RoadFactsMath.RoadClassMinor,
                RoadFactsMath.Classify("Small Roads", "Two-Lane Road"));
        }

        [Fact]
        public void Missing_group_falls_back_to_prefab_name()
        {
            Assert.Equal(
                RoadFactsMath.RoadClassHighway,
                RoadFactsMath.Classify(null, "Two-Lane Two-Way Highway"));
        }

        [Fact]
        public void Missing_group_and_name_stays_unknown()
        {
            Assert.Equal(
                RoadFactsMath.RoadClassUnknown,
                RoadFactsMath.Classify(null, null));
        }

        [Fact]
        public void Speed_converts_baked_limit_to_display_kmh()
        {
            Assert.Equal(100.0, RoadFactsMath.ToKmh(66.66667f));
            Assert.Equal(33.0, RoadFactsMath.ToKmh(22.22222f));
        }
    }
}
