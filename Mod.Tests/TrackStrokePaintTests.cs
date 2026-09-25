using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace CS2MCP
{
    public sealed class TrackStrokePaintTests
    {
        [Fact]
        public void Coarse_rail_is_a_hairline_without_sleepers()
        {
            TrackStrokeRecipe paint = TrackStrokePaint.For(MapStrokeStyle.Rail, 50, MapGrade.Ground);
            Assert.Equal(new MapRgb(0x78, 0x78, 0x78), paint.Body);
            Assert.Equal(0, paint.BodyRadius);
            Assert.False(paint.Hatch);
            Assert.False(paint.Deck);
            Assert.Null(paint.BodyPattern);
        }

        [Fact]
        public void Coarse_rail_tunnel_is_a_short_dash()
        {
            TrackStrokeRecipe paint = TrackStrokePaint.For(MapStrokeStyle.Rail, 50, MapGrade.Tunnel);
            Assert.Equal(new[] { 5, 2 }, paint.BodyPattern);
            Assert.False(paint.Hatch);
        }

        [Fact]
        public void City_rail_uses_a_narrow_white_dash()
        {
            TrackStrokeRecipe paint = TrackStrokePaint.For(MapStrokeStyle.Rail, 10, MapGrade.Ground);
            Assert.Equal(new MapRgb(0x70, 0x70, 0x70), paint.Body);
            Assert.Equal(1, paint.BodyRadius);
            Assert.True(paint.Hatch);
            Assert.Equal(0, paint.HatchRadius);
            Assert.Equal(new[] { 8, 8 }, paint.HatchPattern);
        }

        [Fact]
        public void District_rail_tightens_the_sleeper_gap()
        {
            TrackStrokeRecipe paint = TrackStrokePaint.For(MapStrokeStyle.Rail, 3, MapGrade.Ground);
            Assert.Equal(1, paint.BodyRadius);
            Assert.Equal(0, paint.HatchRadius);
            Assert.Equal(new[] { 0, 8, 8, 1 }, paint.HatchPattern);
        }

        [Fact]
        public void Site_rail_widens_both_strokes()
        {
            TrackStrokeRecipe paint = TrackStrokePaint.For(MapStrokeStyle.Rail, 0.5, MapGrade.Ground);
            Assert.Equal(2, paint.BodyRadius);
            Assert.Equal(1, paint.HatchRadius);
            Assert.Equal(new[] { 0, 8, 8, 1 }, paint.HatchPattern);
        }

        [Fact]
        public void Rail_tunnel_drops_the_white_dash()
        {
            TrackStrokeRecipe city = TrackStrokePaint.For(MapStrokeStyle.Rail, 10, MapGrade.Tunnel);
            Assert.False(city.Hatch);
            Assert.Equal(new MapRgb(0x78, 0x78, 0x78), city.Body);
            Assert.Equal(new[] { 6, 4 }, city.BodyPattern);

            TrackStrokeRecipe site = TrackStrokePaint.For(MapStrokeStyle.Rail, 0.5, MapGrade.Tunnel);
            Assert.False(site.Hatch);
            Assert.Equal(new[] { 8, 6 }, site.BodyPattern);
        }

        [Fact]
        public void Bridge_deck_is_wider_than_the_symbol()
        {
            TrackStrokeRecipe rail = TrackStrokePaint.For(MapStrokeStyle.Rail, 10, MapGrade.Bridge);
            Assert.True(rail.Deck);
            Assert.True(rail.ShellRadius > rail.DeckRadius);
            Assert.True(rail.DeckRadius > rail.BodyRadius);

            TrackStrokeRecipe metro = TrackStrokePaint.For(MapStrokeStyle.Metro, 10, MapGrade.Bridge);
            Assert.True(metro.DeckRadius > metro.BodyRadius);
            Assert.False(metro.Hatch);
        }

        [Fact]
        public void Metro_and_tram_stay_solid_on_the_ground()
        {
            TrackStrokeRecipe metro = TrackStrokePaint.For(MapStrokeStyle.Metro, 10, MapGrade.Ground);
            Assert.Equal(new MapRgb(0x99, 0x99, 0x99), metro.Body);
            Assert.Equal(0, metro.BodyRadius);
            Assert.False(metro.Hatch);
            Assert.Null(metro.BodyPattern);

            TrackStrokeRecipe tram = TrackStrokePaint.For(MapStrokeStyle.Tram, 10, MapGrade.Tunnel);
            Assert.Equal(new MapRgb(0x6E, 0x6E, 0x6E), tram.Body);
            Assert.Equal(new[] { 5, 3 }, tram.BodyPattern);
            Assert.False(tram.Hatch);
        }
    }

    public sealed class MapStrokeRasterTests
    {
        [Fact]
        public void Zero_length_dash_does_not_ink()
        {
            var pixels = new MapRgb[32];
            MapStrokeRaster.Pattern(
                pixels, 32, 1, 0, 0, 31, 0, 0, new MapRgb(255, 255, 255),
                new[] { 0, 8, 8, 1 }, 0, true, true);

            Assert.Equal(0, pixels[0].R);
            Assert.Equal(255, pixels[8].R);
            Assert.Equal(255, pixels[15].R);
            Assert.Equal(0, pixels[16].R);
        }

        [Fact]
        public void Butt_end_does_not_keep_the_disk_past_the_segment()
        {
            var pixels = new MapRgb[64 * 32];
            MapStrokeRaster.Line(pixels, 64, 32, 10, 16, 40, 16, 8, new MapRgb(255, 255, 255), false, false);
            Assert.Equal(255, pixels[16 * 64 + 40].R);
            Assert.Equal(255, pixels[16 * 64 + 24].R);
            Assert.Equal(0, pixels[16 * 64 + 48].R);
            Assert.Equal(255, pixels[(16 + 8) * 64 + 40].R);
        }
    }

    public sealed class MapImagePaintTests
    {
        [Fact]
        public void Site_rail_stays_a_symbol_when_the_prefab_is_wide()
        {
            var stroke = new MapStroke
            {
                Style = MapStrokeStyle.Rail,
                Grade = MapGrade.Ground,
                Layer = 0,
                WidthM = 12.9,
            };
            stroke.X.Add(100);
            stroke.Y.Add(320);
            stroke.X.Add(500);
            stroke.Y.Add(320);
            var frame = new MapFrame { MinX = 0, MinY = 0, MaxX = 640, MaxY = 640 };
            MapImageBuffer buffer = MapImagePaint.Rasterize(
                new List<MapStroke> { stroke }, new List<MapPolygon>(), frame, null);

            double scale = buffer.Width / 640.0;
            int x = 300;
            int y = buffer.Height - 1 - (int)Math.Round(320 * scale);
            int shift = (int)Math.Round(2.0 * scale);
            MapRgb land = MapRgb.FromUnit(0.95f, 0.94f, 0.91f);
            MapRgb north = buffer.Pixels[(y - shift) * buffer.Width + x];
            MapRgb south = buffer.Pixels[(y + shift) * buffer.Width + x];
            Assert.NotEqual(land, north);
            Assert.NotEqual(land, south);
            Assert.Equal(land, buffer.Pixels[y * buffer.Width + x]);
            Assert.Equal(land, buffer.Pixels[(y - shift - 3) * buffer.Width + x]);
        }

        [Fact]
        public void Bridge_shell_stays_on_the_bridge_edge()
        {
            var ground = new MapStroke
            {
                Style = MapStrokeStyle.Medium,
                Grade = MapGrade.Ground,
                WidthM = 16,
            };
            ground.X.Add(40);
            ground.Y.Add(200);
            ground.X.Add(120);
            ground.Y.Add(200);
            var bridge = new MapStroke
            {
                Style = MapStrokeStyle.Medium,
                Grade = MapGrade.Bridge,
                WidthM = 16,
            };
            bridge.X.Add(120);
            bridge.Y.Add(200);
            bridge.X.Add(280);
            bridge.Y.Add(200);
            var frame = new MapFrame { MinX = 0, MinY = 0, MaxX = 640, MaxY = 640 };
            MapImageBuffer buffer = MapImagePaint.Rasterize(
                new List<MapStroke> { ground, bridge }, new List<MapPolygon>(), frame, null);
            double scale = buffer.Width / 640.0;
            int y = buffer.Height - 1 - (int)Math.Round(200 * scale);
            int casing = (int)Math.Round(16 * scale * 0.5) + 1;
            MapRgb fill = MapRgb.FromUnit(0.99f, 0.91f, 0.66f);
            MapRgb land = MapRgb.FromUnit(0.95f, 0.94f, 0.91f);
            MapRgb shell = MapRgb.FromUnit(0.08f, 0.08f, 0.08f);
            MapRgb groundEdge = MapRgb.FromUnit(0.84f, 0.77f, 0.49f);
            int onGround = (int)Math.Round(80 * scale);
            int onBridge = (int)Math.Round(200 * scale);
            Assert.Equal(fill, buffer.Pixels[y * buffer.Width + onGround]);
            Assert.Equal(groundEdge, buffer.Pixels[(y - casing) * buffer.Width + onGround]);
            Assert.Equal(shell, buffer.Pixels[(y - casing) * buffer.Width + onBridge]);
            Assert.Equal(land, buffer.Pixels[(y - casing - 1) * buffer.Width + onBridge]);
            Assert.Equal(land, buffer.Pixels[(y - casing - 1) * buffer.Width + onGround]);
        }

        [Fact]
        public void Higher_road_covers_the_junction_and_ends_are_round()
        {
            var minor = new MapStroke
            {
                Style = MapStrokeStyle.Minor,
                Grade = MapGrade.Ground,
                WidthM = 10,
            };
            minor.X.Add(40);
            minor.Y.Add(200);
            minor.X.Add(200);
            minor.Y.Add(200);
            var medium = new MapStroke
            {
                Style = MapStrokeStyle.Medium,
                Grade = MapGrade.Ground,
                WidthM = 16,
            };
            medium.X.Add(120);
            medium.Y.Add(80);
            medium.X.Add(120);
            medium.Y.Add(320);
            var frame = new MapFrame { MinX = 0, MinY = 0, MaxX = 640, MaxY = 640 };
            MapImageBuffer buffer = MapImagePaint.Rasterize(
                new List<MapStroke> { medium, minor }, new List<MapPolygon>(), frame, null);
            double scale = buffer.Width / 640.0;
            int junctionX = (int)Math.Round(120 * scale);
            int y = buffer.Height - 1 - (int)Math.Round(200 * scale);
            MapRgb mediumFill = MapRgb.FromUnit(0.99f, 0.91f, 0.66f);
            MapRgb minorFill = MapRgb.FromUnit(1f, 1f, 1f);
            Assert.Equal(mediumFill, buffer.Pixels[y * buffer.Width + junctionX]);
            int onMinor = (int)Math.Round(70 * scale);
            Assert.Equal(minorFill, buffer.Pixels[y * buffer.Width + onMinor]);
            int pastEnd = (int)Math.Round(204 * scale);
            Assert.Equal(minorFill, buffer.Pixels[y * buffer.Width + pastEnd]);
        }

        [Fact]
        public void Region_frame_draws_medium_roads()
        {
            var stroke = new MapStroke
            {
                Style = MapStrokeStyle.Medium,
                Grade = MapGrade.Ground,
                WidthM = 20,
            };
            stroke.X.Add(1000);
            stroke.Y.Add(6400);
            stroke.X.Add(6000);
            stroke.Y.Add(6400);
            var frame = new MapFrame { MinX = 0, MinY = 0, MaxX = 12800, MaxY = 12800 };
            MapImageBuffer buffer = MapImagePaint.Rasterize(
                new List<MapStroke> { stroke }, new List<MapPolygon>(), frame, null);
            double scale = buffer.Width / 12800.0;
            int x = (int)Math.Round(3500 * scale);
            int y = buffer.Height - 1 - (int)Math.Round(6400 * scale);
            MapRgb onRoad = buffer.Pixels[y * buffer.Width + x];
            Assert.NotEqual(MapRgb.FromUnit(0.95f, 0.94f, 0.91f), onRoad);
        }

        [Fact]
        public void Citywide_dump_repaints_rails_as_a_symbol()
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow", "Colossal Order", "Cities Skylines II",
                "ModsData", "CitiesSkylines2Agent", "logs", "map-image", "20260925-094400-241");
            if (!File.Exists(Path.Combine(directory, "source.geojson")))
            {
                return;
            }

            var strokes = new List<MapStroke>();
            var fills = new List<MapPolygon>();
            MapFrame frame = MapDump.Load(directory, strokes, fills);
            MapImageBuffer buffer = MapImagePaint.Rasterize(strokes, fills, frame, null);
            string png = Path.Combine(directory, "repaint.png");
            MapPng.Write(png, buffer);

            MapStroke rail = null;
            foreach (MapStroke stroke in strokes)
            {
                if (stroke.Style == MapStrokeStyle.Rail)
                {
                    rail = stroke;
                    break;
                }
            }
            Assert.NotNull(rail);
            double scale = Math.Min(buffer.Width / (frame.MaxX - frame.MinX), buffer.Height / (frame.MaxY - frame.MinY));
            double offsetX = (buffer.Width - (frame.MaxX - frame.MinX) * scale) * 0.5;
            double offsetY = (buffer.Height - (frame.MaxY - frame.MinY) * scale) * 0.5;
            int mid = rail.X.Count / 2;
            int x = (int)Math.Round((rail.X[mid] - frame.MinX) * scale + offsetX);
            int y = buffer.Height - 1 - (int)Math.Round((rail.Y[mid] - frame.MinY) * scale + offsetY);
            Assert.InRange(x, 4, buffer.Width - 5);
            Assert.InRange(y, 4, buffer.Height - 5);
            MapRgb onLine = buffer.Pixels[y * buffer.Width + x];
            MapRgb offLine = buffer.Pixels[y * buffer.Width + Math.Min(buffer.Width - 1, x + 24)];
            Assert.NotEqual(offLine, onLine);
            Assert.True(new FileInfo(png).Length > 1000);
        }

        [Fact]
        public void Repaint_all_exported_maps()
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow", "Colossal Order", "Cities Skylines II",
                "ModsData", "CitiesSkylines2Agent", "logs", "map-image");
            Assert.True(Directory.Exists(root), $"Map export directory missing: {root}");
            string[] directories = Directory.GetDirectories(root);
            Assert.NotEmpty(directories);
            foreach (string directory in directories)
            {
                Assert.True(File.Exists(Path.Combine(directory, "meta.json")), directory);
                Assert.True(File.Exists(Path.Combine(directory, "source.geojson")), directory);
                Repaint(directory);
                Assert.True(new FileInfo(Path.Combine(directory, "repaint.png")).Length > 1000, directory);
            }
        }

        private static void Repaint(string directory)
        {
            if (!File.Exists(Path.Combine(directory, "source.geojson")))
            {
                return;
            }
            var strokes = new List<MapStroke>();
            var fills = new List<MapPolygon>();
            MapFrame frame = MapDump.Load(directory, strokes, fills);
            MapImageBuffer buffer = MapImagePaint.Rasterize(strokes, fills, frame, null);
            MapPng.Write(Path.Combine(directory, "repaint.png"), buffer);
        }
    }
}
