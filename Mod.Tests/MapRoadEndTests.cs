using System;
using System.Collections.Generic;
using Xunit;

namespace CS2MCP
{
    public sealed class MapRoadEndTests
    {
        [Fact]
        public void Open_road_end_has_pavement_at_its_tip_and_casing_on_its_sides()
        {
            MapStroke road = Road(MapGrade.Ground, 40, 120);
            MapImageBuffer image = Render(road);
            int y = PixelY(image, 200);
            int radius = 17;
            MapRgb fill = MapRgb.FromUnit(0.99f, 0.91f, 0.66f);
            MapRgb edge = MapRgb.FromUnit(0.84f, 0.77f, 0.49f);

            Assert.Equal(fill, At(image, 240, y + radius));
            Assert.Equal(edge, At(image, 160, y + radius));
            Assert.Equal(fill, At(image, 256, y));
        }

        [Fact]
        public void Bridge_transition_has_no_black_end_ring()
        {
            MapStroke ground = Road(MapGrade.Ground, 40, 120);
            MapStroke bridge = Road(MapGrade.Bridge, 120, 240);
            MapImageBuffer image = Render(ground, bridge);
            int y = PixelY(image, 200);
            int radius = 17;
            MapRgb fill = MapRgb.FromUnit(0.99f, 0.91f, 0.66f);
            MapRgb shell = MapRgb.FromUnit(0.08f, 0.08f, 0.08f);

            Assert.Equal(fill, At(image, 240, y + radius));
            Assert.Equal(shell, At(image, 280, y + radius));
            Assert.Equal(fill, At(image, 240, y));
        }

        private static MapStroke Road(MapGrade grade, double x0, double x1)
        {
            var road = new MapStroke { Style = MapStrokeStyle.Medium, Grade = grade, WidthM = 16 };
            road.X.Add(x0);
            road.Y.Add(200);
            road.X.Add(x1);
            road.Y.Add(200);
            return road;
        }

        private static MapImageBuffer Render(params MapStroke[] roads)
        {
            var frame = new MapFrame { MinX = 0, MinY = 0, MaxX = 640, MaxY = 640 };
            return MapImagePaint.Rasterize(new List<MapStroke>(roads), new List<MapPolygon>(), frame, null);
        }

        private static int PixelY(MapImageBuffer image, double worldY)
        {
            return image.Height - 1 - (int)Math.Round(worldY * 2);
        }

        private static MapRgb At(MapImageBuffer image, int x, int y)
        {
            return image.Pixels[y * image.Width + x];
        }
    }
}
