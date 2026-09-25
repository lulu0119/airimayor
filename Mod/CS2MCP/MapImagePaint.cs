using System;
using System.Collections.Generic;

namespace CS2MCP
{
    /// <summary>
    /// Paints a map frame into an RGB buffer. Roads keep native meter width.
    /// Rail, metro, and tram use <see cref="TrackStrokePaint"/>.
    /// </summary>
    internal static class MapImagePaint
    {
        internal const int WidthPx = 1280;
        internal const int MaxHeightPx = 1920;

        private static readonly MapRgb Land = MapRgb.FromUnit(0.95f, 0.94f, 0.91f);
        private static readonly MapRgb Water = MapRgb.FromUnit(0.66f, 0.81f, 0.89f);
        private static readonly MapRgb BridgeShell = MapRgb.FromUnit(0.08f, 0.08f, 0.08f);
        private static readonly MapRgb TunnelCasing = MapRgb.FromUnit(0.55f, 0.55f, 0.55f);
        private static readonly MapRgb White = new MapRgb(255, 255, 255);
        private static readonly int[] RoadTunnelDash = { 4, 2 };

        internal static MapScale ScaleOf(MapFrame frame, int pixelWidth)
        {
            double metersPerPixel = (frame.MaxX - frame.MinX) / Math.Max(1, pixelWidth);
            if (metersPerPixel >= 10.0)
            {
                return MapScale.Region;
            }
            if (metersPerPixel >= 3.5)
            {
                return MapScale.City;
            }
            if (metersPerPixel >= 1.0)
            {
                return MapScale.District;
            }
            return MapScale.Site;
        }

        internal static MapImageBuffer Rasterize(
            List<MapStroke> strokes, List<MapPolygon> fills, MapFrame frame,
            Func<double, double, bool> isWater)
        {
            double spanX = frame.MaxX - frame.MinX;
            double spanY = frame.MaxY - frame.MinY;
            int width = WidthPx;
            int height = Math.Max(1, Math.Min(MaxHeightPx, (int)Math.Round(width * spanY / spanX)));
            double scale = Math.Min(width / spanX, height / spanY);
            double offsetX = (width - spanX * scale) * 0.5;
            double offsetY = (height - spanY * scale) * 0.5;
            MapScale mapScale = ScaleOf(frame, width);

            MapRgb[] pixels = PaintBackground(width, height, frame, scale, offsetX, offsetY, isWater);
            foreach (MapPolygon polygon in fills)
            {
                if (polygon.Kind != MapFillKind.Park || !ShowsFill(polygon.Kind, mapScale))
                {
                    continue;
                }
                FillPolygon(pixels, width, height, polygon, frame, scale, offsetX, offsetY, ColorForFill(polygon.Kind));
            }
            foreach (MapPolygon polygon in fills)
            {
                if (polygon.Kind == MapFillKind.Park || !ShowsFill(polygon.Kind, mapScale))
                {
                    continue;
                }
                FillPolygon(pixels, width, height, polygon, frame, scale, offsetX, offsetY, ColorForFill(polygon.Kind));
            }
            DrawStrokes(pixels, width, height, strokes, frame, mapScale, scale, offsetX, offsetY);
            return new MapImageBuffer { Width = width, Height = height, Pixels = pixels };
        }

        /// <summary>
        /// Medium roads stay on at region scale: OSM secondary and tertiary
        /// start near 40 m/pixel, and a citywide frame is about 10.
        /// Small roads wait until the district frame.
        /// </summary>
        private static bool ShowsStroke(MapStrokeStyle style, MapScale mapScale)
        {
            switch (style)
            {
                case MapStrokeStyle.Minor:
                    return mapScale >= MapScale.District;
                case MapStrokeStyle.Tram:
                    return mapScale >= MapScale.City;
                default:
                    return true;
            }
        }

        private static bool ShowsFill(MapFillKind kind, MapScale mapScale)
        {
            switch (kind)
            {
                case MapFillKind.Park:
                    return true;
                case MapFillKind.Residential:
                case MapFillKind.Commercial:
                case MapFillKind.Industrial:
                case MapFillKind.Office:
                    return mapScale >= MapScale.City;
                default:
                    return mapScale >= MapScale.District;
            }
        }

        private static MapRgb[] PaintBackground(
            int width, int height, MapFrame frame, double scale, double offsetX, double offsetY,
            Func<double, double, bool> isWater)
        {
            var pixels = new MapRgb[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Land;
            }
            if (isWater == null)
            {
                return pixels;
            }
            for (int row = 0; row < height; row += 2)
            {
                for (int col = 0; col < width; col += 2)
                {
                    double worldX = frame.MinX + (col + 0.5 - offsetX) / scale;
                    double worldY = frame.MinY + (height - 1 - row + 0.5 - offsetY) / scale;
                    if (!isWater(worldX, worldY))
                    {
                        continue;
                    }
                    for (int dy = 0; dy < 2 && row + dy < height; dy++)
                    {
                        for (int dx = 0; dx < 2 && col + dx < width; dx++)
                        {
                            pixels[(row + dy) * width + col + dx] = Water;
                        }
                    }
                }
            }
            return pixels;
        }

        /// <summary>
        /// Connectivity first: every way is painted as ground. Off-ground
        /// spans are a tube overlay on the same vertices. A positive layer is
        /// a full tube so stacked ramps stay countable.
        /// </summary>
        private static void DrawStrokes(
            MapRgb[] pixels, int width, int height, List<MapStroke> strokes,
            MapFrame frame, MapScale mapScale, double scale, double offsetX, double offsetY)
        {
            DrawTunnels(pixels, width, height, strokes, frame, mapScale, scale, offsetX, offsetY);
            DrawLayer(pixels, width, height, strokes, frame, mapScale, scale, offsetX, offsetY, 0);
            int top = 0;
            foreach (MapStroke stroke in strokes)
            {
                if (stroke.Layer > top)
                {
                    top = stroke.Layer;
                }
            }
            for (int layer = 1; layer <= top; layer++)
            {
                DrawLayer(pixels, width, height, strokes, frame, mapScale, scale, offsetX, offsetY, layer);
            }
        }

        /// <summary>
        /// Each class is shell, then casing, then fill, from minor up to
        /// highway. A higher road is painted later, so the crossing keeps
        /// its color. Pavement stays one run across ground and bridge.
        /// </summary>
        private static void DrawLayer(
            MapRgb[] pixels, int width, int height, List<MapStroke> strokes,
            MapFrame frame, MapScale mapScale, double scale, double offsetX, double offsetY, int layer)
        {
            foreach (MapStrokeStyle style in StyleOrder)
            {
                foreach (MapStroke stroke in strokes)
                {
                    if (!ShowsInLayer(stroke, mapScale, layer, style)
                        || stroke.Grade != MapGrade.Bridge
                        || stroke.X.Count < 2)
                    {
                        continue;
                    }
                    DrawBridgeUnderlay(pixels, width, height, stroke, frame, scale, offsetX, offsetY,
                        0, stroke.X.Count - 1);
                }
                foreach (MapStroke stroke in strokes)
                {
                    if (!ShowsInLayer(stroke, mapScale, layer, style)
                        || stroke.Grade == MapGrade.Tunnel
                        || stroke.X.Count < 2)
                    {
                        continue;
                    }
                    DrawCasing(pixels, width, height, stroke, frame, scale, offsetX, offsetY,
                        MapGrade.Ground, 0, stroke.X.Count - 1);
                }
                foreach (MapStroke stroke in strokes)
                {
                    if (!ShowsInLayer(stroke, mapScale, layer, style)
                        || stroke.Grade == MapGrade.Tunnel
                        || stroke.X.Count < 2)
                    {
                        continue;
                    }
                    DrawFill(pixels, width, height, stroke, frame, scale, offsetX, offsetY,
                        MapGrade.Ground, 0, stroke.X.Count - 1);
                }
            }
        }

        private static bool ShowsInLayer(MapStroke stroke, MapScale mapScale, int layer, MapStrokeStyle style)
        {
            return stroke.Style == style && stroke.Layer == layer && ShowsStroke(stroke.Style, mapScale);
        }

        private static readonly MapStrokeStyle[] StyleOrder =
        {
            MapStrokeStyle.Minor,
            MapStrokeStyle.Medium,
            MapStrokeStyle.Large,
            MapStrokeStyle.Highway,
            MapStrokeStyle.Rail,
            MapStrokeStyle.Metro,
            MapStrokeStyle.Tram,
        };

        private static void DrawTunnels(
            MapRgb[] pixels, int width, int height, List<MapStroke> strokes,
            MapFrame frame, MapScale mapScale, double scale, double offsetX, double offsetY)
        {
            foreach (MapStrokeStyle style in StyleOrder)
            {
                foreach (MapStroke stroke in strokes)
                {
                    if (stroke.Style != style || !ShowsStroke(stroke.Style, mapScale)
                        || stroke.Grade != MapGrade.Tunnel
                        || stroke.X.Count < 2)
                    {
                        continue;
                    }
                    DrawCasing(pixels, width, height, stroke, frame, scale, offsetX, offsetY,
                        MapGrade.Tunnel, 0, stroke.X.Count - 1);
                }
                foreach (MapStroke stroke in strokes)
                {
                    if (stroke.Style != style || !ShowsStroke(stroke.Style, mapScale)
                        || stroke.Grade != MapGrade.Tunnel
                        || stroke.X.Count < 2)
                    {
                        continue;
                    }
                    DrawFill(pixels, width, height, stroke, frame, scale, offsetX, offsetY,
                        MapGrade.Tunnel, 0, stroke.X.Count - 1);
                }
            }
        }

        private static void DrawBridgeUnderlay(
            MapRgb[] pixels, int width, int height, MapStroke stroke,
            MapFrame frame, double scale, double offsetX, double offsetY, int from, int to)
        {
            if (TrackStrokePaint.IsTrack(stroke.Style))
            {
                TrackStrokeRecipe track = TrackStrokePaint.For(stroke.Style, 1.0 / scale, MapGrade.Bridge);
                if (!track.Deck)
                {
                    return;
                }
                int extra = (int)Math.Round(TrackStrokePaint.LateralMeters(stroke.Style, stroke.WidthM, scale) * scale);
                double sideM = Math.Max(1, track.ShellRadius + extra) / scale;
                DrawStroke(pixels, width, height, stroke, frame, scale, offsetX, offsetY,
                    TrackStrokePaint.Shell, 0, null, from, to, sideM);
                DrawStroke(pixels, width, height, stroke, frame, scale, offsetX, offsetY,
                    TrackStrokePaint.Shell, 0, null, from, to, -sideM);
            }
        }

        private static void DrawCasing(
            MapRgb[] pixels, int width, int height, MapStroke stroke,
            MapFrame frame, double scale, double offsetX, double offsetY,
            MapGrade grade, int from, int to)
        {
            if (TrackStrokePaint.IsTrack(stroke.Style))
            {
                return;
            }

            StrokePaint paint = PaintFor(stroke.Style, grade);
            int radius = StrokePixelRadius(stroke.WidthM, scale, stroke.Style);
            if (radius < 1 || !paint.Casing)
            {
                return;
            }
            bool tunnel = grade == MapGrade.Tunnel;
            MapRgb casing = tunnel
                ? TunnelCasing
                : stroke.Grade == MapGrade.Bridge ? BridgeShell : paint.CasingColor;
            DrawStroke(pixels, width, height, stroke, frame, scale, offsetX, offsetY,
                casing, radius + 1, tunnel ? RoadTunnelDash : null, from, to, 0);
        }

        private static void DrawFill(
            MapRgb[] pixels, int width, int height, MapStroke stroke,
            MapFrame frame, double scale, double offsetX, double offsetY,
            MapGrade grade, int from, int to)
        {
            if (TrackStrokePaint.IsTrack(stroke.Style))
            {
                TrackStrokeRecipe track = TrackStrokePaint.For(stroke.Style, 1.0 / scale, grade);
                double lateral = TrackStrokePaint.LateralMeters(stroke.Style, stroke.WidthM, scale);
                DrawTrackSymbol(pixels, width, height, stroke, frame, scale, offsetX, offsetY, track, from, to, -lateral);
                if (lateral > 0)
                {
                    DrawTrackSymbol(pixels, width, height, stroke, frame, scale, offsetX, offsetY, track, from, to, lateral);
                }
                return;
            }

            StrokePaint paint = PaintFor(stroke.Style, grade);
            int radius = StrokePixelRadius(stroke.WidthM, scale, stroke.Style);
            DrawStroke(pixels, width, height, stroke, frame, scale, offsetX, offsetY,
                paint.Color, radius, null, from, to, 0);
            // Cover the casing's round end without erasing its side edges.
            if (radius >= 1 && paint.Casing && from == 0 && stroke.CapStart)
            {
                PaintRoadEnd(pixels, width, height, stroke, frame, scale, offsetX, offsetY,
                    from, radius + 1, paint.Color);
            }
            if (radius >= 1 && paint.Casing && to == stroke.X.Count - 1 && stroke.CapEnd)
            {
                PaintRoadEnd(pixels, width, height, stroke, frame, scale, offsetX, offsetY,
                    to, radius + 1, paint.Color);
            }
        }

        private static void PaintRoadEnd(
            MapRgb[] pixels, int width, int height, MapStroke stroke,
            MapFrame frame, double scale, double offsetX, double offsetY,
            int index, int radius, MapRgb color)
        {
            World(stroke, frame, scale, offsetX, offsetY, height, index, 0, out int x, out int y);
            MapStrokeRaster.Line(pixels, width, height, x, y, x, y, radius, color, true, true);
        }

        private static void DrawTrackSymbol(
            MapRgb[] pixels, int width, int height, MapStroke stroke,
            MapFrame frame, double scale, double offsetX, double offsetY,
            TrackStrokeRecipe track, int from, int to, double lateralM)
        {
            DrawStroke(pixels, width, height, stroke, frame, scale, offsetX, offsetY,
                track.Body, track.BodyRadius, track.BodyPattern, from, to, lateralM);
            if (!track.Hatch)
            {
                return;
            }
            DrawStroke(pixels, width, height, stroke, frame, scale, offsetX, offsetY,
                TrackStrokePaint.White, track.HatchRadius, track.HatchPattern, from, to, lateralM);
        }

        private struct StrokePaint
        {
            public MapRgb Color;
            public bool Casing;
            public MapRgb CasingColor;
        }

        private static StrokePaint PaintFor(MapStrokeStyle style, MapGrade grade)
        {
            StrokePaint paint;
            switch (style)
            {
                case MapStrokeStyle.Highway:
                    paint = new StrokePaint
                    {
                        Color = MapRgb.FromUnit(0.910f, 0.573f, 0.635f),
                        Casing = true,
                        CasingColor = MapRgb.FromUnit(0.863f, 0.165f, 0.404f),
                    };
                    break;
                case MapStrokeStyle.Large:
                    paint = new StrokePaint
                    {
                        Color = MapRgb.FromUnit(0.98f, 0.76f, 0.31f),
                        Casing = true,
                        CasingColor = MapRgb.FromUnit(0.85f, 0.62f, 0.17f),
                    };
                    break;
                case MapStrokeStyle.Medium:
                    paint = new StrokePaint
                    {
                        Color = MapRgb.FromUnit(0.99f, 0.91f, 0.66f),
                        Casing = true,
                        CasingColor = MapRgb.FromUnit(0.84f, 0.77f, 0.49f),
                    };
                    break;
                default:
                    paint = new StrokePaint
                    {
                        Color = White,
                        Casing = true,
                        CasingColor = MapRgb.FromUnit(0.80f, 0.78f, 0.73f),
                    };
                    break;
            }
            if (grade == MapGrade.Tunnel)
            {
                paint.Color = MapRgb.Lerp(paint.Color, White, 0.12f);
            }
            return paint;
        }

        private static MapRgb ColorForFill(MapFillKind kind)
        {
            switch (kind)
            {
                case MapFillKind.Park:
                    return MapRgb.FromUnit(0.78f, 0.95f, 0.80f);
                case MapFillKind.Residential:
                    return MapRgb.FromUnit(0.88f, 0.87f, 0.87f);
                case MapFillKind.Commercial:
                    return MapRgb.FromUnit(0.95f, 0.85f, 0.85f);
                case MapFillKind.Industrial:
                    return MapRgb.FromUnit(0.92f, 0.86f, 0.91f);
                case MapFillKind.Office:
                    return MapRgb.FromUnit(0.93f, 0.88f, 0.82f);
                case MapFillKind.Service:
                    return MapRgb.FromUnit(0.82f, 0.80f, 0.76f);
                default:
                    return MapRgb.FromUnit(0.77f, 0.73f, 0.64f);
            }
        }

        /// <summary>
        /// Half-width in pixels from native meters. Class floor keeps a
        /// citywide highway a few pixels wide. Tracks do not use this.
        /// </summary>
        private static int StrokePixelRadius(double widthM, double scale, MapStrokeStyle style)
        {
            int fromMeters = 0;
            if (widthM > 0.0 && scale > 0.0)
            {
                fromMeters = Math.Max(0, (int)Math.Round(widthM * scale * 0.5));
            }
            int floor = style == MapStrokeStyle.Highway
                || style == MapStrokeStyle.Large
                || style == MapStrokeStyle.Medium
                ? 1
                : 0;
            return Math.Max(fromMeters, floor);
        }

        private static void DrawStroke(
            MapRgb[] pixels, int width, int height, MapStroke stroke,
            MapFrame frame, double scale, double offsetX, double offsetY,
            MapRgb color, int radius, int[] pattern, int from, int to, double lateralM)
        {
            if (to <= from)
            {
                return;
            }
            int along = 0;
            for (int i = from; i < to; i++)
            {
                int j = i + 1;
                World(stroke, frame, scale, offsetX, offsetY, height, i, lateralM, out int x0, out int y0);
                World(stroke, frame, scale, offsetX, offsetY, height, j, lateralM, out int x1, out int y1);
                if (frame.Clip && IsOutsideFrame(x0, y0, x1, y1, width, height))
                {
                    continue;
                }
                along = MapStrokeRaster.Pattern(
                    pixels, width, height, x0, y0, x1, y1, radius, color, pattern, along, true, true);
            }
        }

        private static void World(
            MapStroke stroke, MapFrame frame, double scale, double offsetX, double offsetY, int height,
            int index, double lateralM, out int x, out int y)
        {
            double wx = stroke.X[index];
            double wy = stroke.Y[index];
            if (lateralM != 0)
            {
                int last = stroke.X.Count - 1;
                int prev = index == 0 ? 0 : index - 1;
                int next = index == last ? last : index + 1;
                double dx = stroke.X[next] - stroke.X[prev];
                double dy = stroke.Y[next] - stroke.Y[prev];
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length > 1e-6)
                {
                    wx += -dy / length * lateralM;
                    wy += dx / length * lateralM;
                }
            }
            x = (int)Math.Round((wx - frame.MinX) * scale + offsetX);
            y = height - 1 - (int)Math.Round((wy - frame.MinY) * scale + offsetY);
        }

        private static bool IsOutsideFrame(int x0, int y0, int x1, int y1, int width, int height)
        {
            return (x0 < 0 && x1 < 0) || (x0 >= width && x1 >= width)
                || (y0 < 0 && y1 < 0) || (y0 >= height && y1 >= height);
        }

        private static void FillPolygon(
            MapRgb[] pixels, int width, int height, MapPolygon polygon,
            MapFrame frame, double scale, double offsetX, double offsetY,
            MapRgb color)
        {
            int n = polygon.X.Count;
            if (n < 3)
            {
                return;
            }
            var xs = new int[n];
            var ys = new int[n];
            for (int i = 0; i < n; i++)
            {
                xs[i] = (int)Math.Round((polygon.X[i] - frame.MinX) * scale + offsetX);
                ys[i] = height - 1 - (int)Math.Round((polygon.Y[i] - frame.MinY) * scale + offsetY);
            }
            int rowMin = ys[0];
            int rowMax = ys[0];
            for (int i = 1; i < n; i++)
            {
                if (ys[i] < rowMin)
                {
                    rowMin = ys[i];
                }
                if (ys[i] > rowMax)
                {
                    rowMax = ys[i];
                }
            }
            rowMin = Math.Max(rowMin, 0);
            rowMax = Math.Min(rowMax, height - 1);
            var crossings = new List<int>(8);
            for (int row = rowMin; row <= rowMax; row++)
            {
                crossings.Clear();
                for (int i = 0; i < n; i++)
                {
                    int x0 = xs[i];
                    int y0 = ys[i];
                    int x1 = xs[(i + 1) % n];
                    int y1 = ys[(i + 1) % n];
                    if ((y0 <= row && y1 > row) || (y1 <= row && y0 > row))
                    {
                        crossings.Add(x0 + (int)((long)(row - y0) * (x1 - x0) / (y1 - y0)));
                    }
                }
                crossings.Sort();
                for (int k = 0; k + 1 < crossings.Count; k += 2)
                {
                    int from = Math.Max(crossings[k], 0);
                    int to = Math.Min(crossings[k + 1], width - 1);
                    int rowStart = row * width;
                    for (int x = from; x <= to; x++)
                    {
                        pixels[rowStart + x] = color;
                    }
                }
            }
        }
    }
}
