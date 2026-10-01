using System.Collections.Generic;
using airimayor.Host;
using Xunit;

namespace airimayor.Host
{
    public sealed class PointedPlaceTextTests
    {
        [Fact]
        public void A_point_alone_is_the_whole_message()
        {
            string text = PointedPlaceText.Compose("", new List<PointedPlace>
            {
                new PointedPlace { Kind = "point", Name = "Point 1", X = 455.2f, Z = -70.4f },
            });

            Assert.Equal("Pointed at:\n- Point 1 at (455, -70) m", text);
        }

        [Fact]
        public void Words_precede_the_places()
        {
            string text = PointedPlaceText.Compose("Build a school here.", new List<PointedPlace>
            {
                new PointedPlace
                {
                    Kind = "building",
                    Name = "Elementary School",
                    X = 412f,
                    Z = -88f,
                    Index = 18432,
                    Version = 7,
                },
                new PointedPlace
                {
                    Kind = "road",
                    Name = "Small Road",
                    X = 400f,
                    Z = -100f,
                    EndX = 480f,
                    EndZ = -100f,
                    HasEnd = true,
                    Index = 2201,
                    Version = 3,
                },
            });

            Assert.Equal(
                "Build a school here.\n\nPointed at:\n"
                + "- Elementary School at (412, -88) m (building 18432.7)\n"
                + "- Small Road from (400, -100) to (480, -100) m (road 2201.3)",
                text);
        }

        [Fact]
        public void A_place_round_trips_through_json()
        {
            string json = "{\"id\":\"p1\",\"kind\":\"point\",\"name\":\"Point 1\",\"x\":12.4,\"z\":8,\"index\":0,\"version\":0}";
            Assert.True(PointedPlaceText.TryParse(json, out PointedPlace parsed));
            Assert.Equal("Point 1", parsed.Name);
            Assert.Equal(12.4f, parsed.X, 3);
            Assert.False(parsed.HasEnd);
        }
    }
}
