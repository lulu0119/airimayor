using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using CitiesSkylines2Agent.Host;
using Game.Simulation;
using Newtonsoft.Json;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace CS2MCP
{
    /// <summary>
    /// Undistorted city map rasterized from native ECS geometry in game
    /// meters. Optional map range matches map_text; names stay in map_text.
    /// Citywide by default; a requested extent clips directly.
    /// Read-only: runs on the simulation thread like other perception tools.
    /// Shares the vision switch with screenshot (gated in AgentToolSurface).
    /// </summary>
    public sealed partial class RequestHandlers
    {
        private const int kMapWidth = 1280;
        private const int kMapMaxHeight = 1920;

        private BridgeResponse MapImage(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse cityError))
            {
                return cityError;
            }

            bool hasBounds = request.Query.ContainsKey("x") || request.Query.ContainsKey("z")
                || request.Query.ContainsKey("radius") || request.Query.ContainsKey("xMin")
                || request.Query.ContainsKey("zMin") || request.Query.ContainsKey("xMax")
                || request.Query.ContainsKey("zMax");
            float xMin = 0f;
            float zMin = 0f;
            float xMax = 0f;
            float zMax = 0f;
            if (hasBounds
                && !TryGetWorldBounds(request, out xMin, out zMin, out xMax, out zMax, out BridgeResponse boundsError))
            {
                return boundsError;
            }

            var strokes = new List<MapStroke>();
            var fills = new List<MapPolygon>();
            var collectTimer = Stopwatch.StartNew();
            if (!TryCollectNativeMapGeometry(
                    hasBounds ? xMin : (float?)null,
                    hasBounds ? zMin : (float?)null,
                    hasBounds ? xMax : (float?)null,
                    hasBounds ? zMax : (float?)null,
                    strokes, fills, out string collectError))
            {
                return BridgeResponse.Error(BridgeErrorKind.Internal,
                    "map geometry collection failed"
                    + (string.IsNullOrEmpty(collectError) ? "" : ": " + collectError));
            }
            collectTimer.Stop();
            MapLayering.Result layering = AssignRoadLayers(strokes);
            JoinDrawnStrokes(strokes);
            MapFrame frame = hasBounds
                ? MapFrame.FromBounds(xMin, zMin, xMax, zMax)
                : MapFrame.FromData(strokes, fills) ?? MapFrame.World();
            MapScale mapScale = ScaleOf(frame, kMapWidth);
            Mod.Log.Info($"map_image: {strokes.Count} strokes, {fills.Count} footprints, {mapScale}" +
                (hasBounds ? $" (extent {xMin},{zMin} to {xMax},{zMax})" : " (citywide)") +
                $" in {collectTimer.ElapsedMilliseconds}ms");

            Texture2D texture = null;
            try
            {
                texture = Rasterize(strokes, fills, frame, mapScale, BuildWaterSampler());
                byte[] png = ImageConversion.EncodeToPNG(texture);
                if (png == null || png.Length == 0)
                {
                    return BridgeResponse.Error(BridgeErrorKind.Internal, "map PNG encode failed");
                }
                DumpMapImage(strokes, fills, frame, mapScale, layering, png);
                return BridgeResponse.Png(png, ToolPreview.EncodeThumbnail(texture));
            }
            catch (Exception e)
            {
                return BridgeResponse.Error(BridgeErrorKind.Internal,
                    $"map render failed: {e.GetType().Name}: {e.Message}");
            }
            finally
            {
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }
        }

        /// <summary>
        /// Grade-separation solver still assigns integer layers inside a
        /// grade (stacked ramps). Tunnel / ground / bridge order is a
        /// separate OSM Carto pass.
        /// </summary>
        private static MapLayering.Result AssignRoadLayers(List<MapStroke> strokes)
        {
            var features = new List<MapLayering.Feature>(strokes.Count);
            foreach (MapStroke stroke in strokes)
            {
                features.Add(new MapLayering.Feature
                {
                    X = stroke.X,
                    Y = stroke.Y,
                    Elev = stroke.HasElev ? stroke.Elev : 0.0,
                    HasElev = stroke.HasElev,
                });
            }
            MapLayering.Result result = MapLayering.ComputeLayers(features);
            for (int i = 0; i < strokes.Count && i < result.Layers.Length; i++)
            {
                strokes[i].Layer = result.Layers[i];
            }
            if (!result.Converged)
            {
                Mod.Log.Warn("map_image: road layer constraints did not converge; " +
                    "deep stacks are clamped at the top layer");
            }
            if (result.SkippedCells > 0)
            {
                Mod.Log.Warn($"map_image: skipped {result.SkippedCells} overfull hash cells; " +
                    "nearby overpasses may draw flat");
            }
            return result;
        }

        private static void JoinDrawnStrokes(List<MapStroke> strokes)
        {
            var pieces = new List<MapStrokeJoin.Piece>(strokes.Count);
            foreach (MapStroke stroke in strokes)
            {
                pieces.Add(new MapStrokeJoin.Piece
                {
                    Style = (int)stroke.Style,
                    Layer = stroke.Layer,
                    WidthM = stroke.WidthM,
                    Elev = stroke.Elev,
                    HasElev = stroke.HasElev,
                    StartNode = stroke.StartNode,
                    EndNode = stroke.EndNode,
                    CapStart = stroke.CapStart,
                    CapEnd = stroke.CapEnd,
                    X = stroke.X,
                    Y = stroke.Y,
                    Rel = stroke.Rel,
                });
            }
            List<MapStrokeJoin.Piece> joined = MapStrokeJoin.Join(pieces);
            strokes.Clear();
            foreach (MapStrokeJoin.Piece piece in joined)
            {
                var stroke = new MapStroke
                {
                    Style = (MapStrokeStyle)piece.Style,
                    Grade = DominantGrade(piece.Rel),
                    Layer = piece.Layer,
                    WidthM = piece.WidthM,
                    Elev = piece.Elev,
                    HasElev = piece.HasElev,
                    StartNode = piece.StartNode,
                    EndNode = piece.EndNode,
                    CapStart = piece.CapStart,
                    CapEnd = piece.CapEnd,
                };
                stroke.X.AddRange(piece.X);
                stroke.Y.AddRange(piece.Y);
                stroke.Rel.AddRange(piece.Rel);
                strokes.Add(stroke);
            }
        }

        /// <summary>
        /// OSM Carto density for this PNG, from meters/pixel of the rendered
        /// frame — not a model-facing zoom argument.
        /// </summary>
        private enum MapScale
        {
            Region,
            City,
            District,
            Site,
        }

        private enum MapStrokeStyle
        {
            Minor,
            Medium,
            Large,
            Highway,
            Rail,
            Metro,
            Tram,
        }

        private enum MapGrade
        {
            Tunnel,
            Ground,
            Bridge,
        }

        private enum MapFillKind
        {
            Building,
            Residential,
            Commercial,
            Industrial,
            Office,
            Park,
            Service,
        }

        private sealed class MapStroke
        {
            public MapStrokeStyle Style;
            public MapGrade Grade;
            public double WidthM;
            public double Elev = double.NaN;
            public bool HasElev;
            public int Layer;
            public long StartNode;
            public long EndNode;
            public bool CapStart = true;
            public bool CapEnd = true;
            public readonly List<double> X = new List<double>();
            public readonly List<double> Y = new List<double>();
            public readonly List<float> Rel = new List<float>();
        }

        private sealed class MapPolygon
        {
            public MapFillKind Kind;
            public readonly List<double> X = new List<double>();
            public readonly List<double> Y = new List<double>();
        }

        private sealed class MapFrame
        {
            public double MinX;
            public double MinY;
            public double MaxX;
            public double MaxY;
            public bool Clip;

            public static MapFrame FromData(List<MapStroke> strokes, List<MapPolygon> fills)
            {
                double minX = double.PositiveInfinity;
                double minY = double.PositiveInfinity;
                double maxX = double.NegativeInfinity;
                double maxY = double.NegativeInfinity;
                AccumulateBounds(strokes, fills, ref minX, ref minY, ref maxX, ref maxY);
                if (!(maxX > minX) || !(maxY > minY))
                {
                    return null;
                }
                return new MapFrame { MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY };
            }

            public static MapFrame FromBounds(float xMin, float zMin, float xMax, float zMax)
            {
                return new MapFrame { MinX = xMin, MinY = zMin, MaxX = xMax, MaxY = zMax, Clip = true };
            }

            public static MapFrame World()
            {
                return new MapFrame
                {
                    MinX = -kWorldHalfSize,
                    MinY = -kWorldHalfSize,
                    MaxX = kWorldHalfSize,
                    MaxY = kWorldHalfSize,
                };
            }
        }

        private static Texture2D Rasterize(
            List<MapStroke> strokes, List<MapPolygon> fills, MapFrame frame,
            MapScale mapScale, Func<double, double, bool> isWater)
        {
            double spanX = frame.MaxX - frame.MinX;
            double spanY = frame.MaxY - frame.MinY;
            int width = kMapWidth;
            int height = Mathf.Clamp(Mathf.RoundToInt((float)(width * spanY / spanX)), 1, kMapMaxHeight);
            double scale = Math.Min(width / spanX, height / spanY);
            double offsetX = (width - spanX * scale) * 0.5;
            double offsetY = (height - spanY * scale) * 0.5;

            Color[] pixels = PaintBackground(width, height, frame, scale, offsetX, offsetY, isWater);
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

            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static MapScale ScaleOf(MapFrame frame, int pixelWidth)
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

        private static bool ShowsStroke(MapStrokeStyle style, MapScale mapScale)
        {
            switch (style)
            {
                case MapStrokeStyle.Minor:
                    return mapScale >= MapScale.District;
                case MapStrokeStyle.Medium:
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

        /// <summary>
        /// World-space water test built on the simulation thread. Null when
        /// unavailable: the map stays land-only rather than failing.
        /// </summary>
        private Func<double, double, bool> BuildWaterSampler()
        {
            try
            {
                WaterSystem water = World.GetOrCreateSystemManaged<WaterSystem>();
                WaterSurfaceData<SurfaceWater> surface = water.GetSurfaceData(out JobHandle deps);
                deps.Complete();
                return (double worldX, double worldY) =>
                {
                    try
                    {
                        var position = new float3((float)worldX, 0f, (float)worldY);
                        return WaterUtils.SampleDepth(ref surface, position) > 0.05f;
                    }
                    catch
                    {
                        return false;
                    }
                };
            }
            catch
            {
                return null;
            }
        }

        private static Color[] PaintBackground(
            int width, int height, MapFrame frame, double scale, double offsetX, double offsetY,
            Func<double, double, bool> isWater)
        {
            var land = new Color(0.95f, 0.94f, 0.91f);
            var water = new Color(0.66f, 0.81f, 0.89f);
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = land;
            }

            if (isWater != null)
            {
                for (int row = 0; row < height; row += 2)
                {
                    for (int col = 0; col < width; col += 2)
                    {
                        double worldX = frame.MinX + (col + 0.5 - offsetX) / scale;
                        double worldY = frame.MinY + (height - 1 - row + 0.5 - offsetY) / scale;
                        bool wet = isWater(worldX, worldY);
                        if (!wet)
                        {
                            continue;
                        }
                        for (int dy = 0; dy < 2 && row + dy < height; dy++)
                        {
                            for (int dx = 0; dx < 2 && col + dx < width; dx++)
                            {
                                pixels[(row + dy) * width + col + dx] = water;
                            }
                        }
                    }
                }
            }
            return pixels;
        }

        /// <summary>
        /// Connectivity first: every way is painted as ground. Off-ground
        /// spans are a tube overlay on the same vertices. Layer > 0 ways are
        /// full tubes so stacked ramps stay countable.
        /// </summary>
        private static void DrawStrokes(
            Color[] pixels, int width, int height, List<MapStroke> strokes,
            MapFrame frame, MapScale mapScale, double scale, double offsetX, double offsetY)
        {
            DrawSpans(pixels, width, height, strokes, frame, mapScale, scale, offsetX, offsetY, MapGrade.Tunnel, tube: false);
            DrawWaysBatched(pixels, width, height, strokes, frame, mapScale, scale, offsetX, offsetY, layer: 0);
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
                DrawWaysTubed(pixels, width, height, strokes, frame, mapScale, scale, offsetX, offsetY, layer);
            }
            DrawSpans(pixels, width, height, strokes, frame, mapScale, scale, offsetX, offsetY, MapGrade.Bridge, tube: true);
        }

        private static void DrawWaysBatched(
            Color[] pixels, int width, int height, List<MapStroke> strokes,
            MapFrame frame, MapScale mapScale, double scale, double offsetX, double offsetY, int layer)
        {
            foreach (MapStroke stroke in strokes)
            {
                if (!ShowsStroke(stroke.Style, mapScale) || stroke.Layer != layer)
                {
                    continue;
                }
                ForEachSpan(stroke, (from, to, grade) =>
                {
                    if (grade == MapGrade.Tunnel)
                    {
                        return;
                    }
                    bool capStart = from == 0 && stroke.CapStart;
                    bool capEnd = to == stroke.X.Count - 1 && stroke.CapEnd;
                    DrawRoadCasing(pixels, width, height, stroke, frame, scale, offsetX, offsetY, MapGrade.Ground, false, from, to, capStart, capEnd);
                });
            }
            foreach (MapStroke stroke in strokes)
            {
                if (!ShowsStroke(stroke.Style, mapScale) || stroke.Layer != layer)
                {
                    continue;
                }
                ForEachSpan(stroke, (from, to, grade) =>
                {
                    if (grade == MapGrade.Tunnel)
                    {
                        return;
                    }
                    bool capStart = from == 0 && stroke.CapStart;
                    bool capEnd = to == stroke.X.Count - 1 && stroke.CapEnd;
                    DrawRoadFill(pixels, width, height, stroke, frame, mapScale, scale, offsetX, offsetY, MapGrade.Ground, from, to, capStart, capEnd);
                });
            }
        }

        private static void DrawWaysTubed(
            Color[] pixels, int width, int height, List<MapStroke> strokes,
            MapFrame frame, MapScale mapScale, double scale, double offsetX, double offsetY, int layer)
        {
            foreach (MapStroke stroke in strokes)
            {
                if (!ShowsStroke(stroke.Style, mapScale) || stroke.Layer != layer)
                {
                    continue;
                }
                DrawRoadCasing(pixels, width, height, stroke, frame, scale, offsetX, offsetY, MapGrade.Bridge, true, 0, stroke.X.Count - 1, stroke.CapStart, stroke.CapEnd);
                DrawRoadFill(pixels, width, height, stroke, frame, mapScale, scale, offsetX, offsetY, MapGrade.Bridge, 0, stroke.X.Count - 1, stroke.CapStart, stroke.CapEnd);
            }
        }

        private static void DrawSpans(
            Color[] pixels, int width, int height, List<MapStroke> strokes,
            MapFrame frame, MapScale mapScale, double scale, double offsetX, double offsetY,
            MapGrade grade, bool tube)
        {
            foreach (MapStroke stroke in strokes)
            {
                if (!ShowsStroke(stroke.Style, mapScale))
                {
                    continue;
                }
                if (tube && stroke.Layer > 0)
                {
                    continue;
                }
                int last = stroke.X.Count - 1;
                ForEachSpan(stroke, (from, to, spanGrade) =>
                {
                    if (spanGrade != grade)
                    {
                        return;
                    }
                    bool capStart = from == 0 && stroke.CapStart;
                    bool capEnd = to == last && stroke.CapEnd;
                    DrawRoadCasing(pixels, width, height, stroke, frame, scale, offsetX, offsetY, grade, tube, from, to, capStart, capEnd);
                    DrawRoadFill(pixels, width, height, stroke, frame, mapScale, scale, offsetX, offsetY, grade, from, to, capStart, capEnd);
                });
            }
        }

        private static void ForEachSpan(MapStroke stroke, Action<int, int, MapGrade> emit)
        {
            int last = stroke.X.Count - 1;
            if (last < 1)
            {
                return;
            }
            int i = 0;
            while (i < last)
            {
                MapGrade spanGrade = SegmentGrade(stroke, i);
                int j = i + 1;
                while (j < last && SegmentGrade(stroke, j) == spanGrade)
                {
                    j++;
                }
                emit(i, j, spanGrade);
                i = j;
            }
        }

        private static MapGrade SegmentGrade(MapStroke stroke, int i)
        {
            if (stroke.Rel.Count != stroke.X.Count || i + 1 >= stroke.Rel.Count)
            {
                return stroke.Grade;
            }
            return GradeFromRelative(0.5f * (stroke.Rel[i] + stroke.Rel[i + 1]));
        }

        private static readonly Color BridgeShell = new Color(0.08f, 0.08f, 0.08f);
        private static readonly Color TunnelCasing = new Color(0.55f, 0.55f, 0.55f);
        private static readonly Color RailDash = Color.white;

        private static void DrawRoadCasing(
            Color[] pixels, int width, int height, MapStroke stroke,
            MapFrame frame, double scale, double offsetX, double offsetY,
            MapGrade grade, bool shell, int from, int to, bool capStart, bool capEnd)
        {
            StrokePaint paint = PaintForGrade(stroke.Style, grade);
            int radius = StrokePixelRadius(stroke.WidthM, scale, stroke.Style);
            if (radius < 1)
            {
                return;
            }
            if (shell && radius >= 2)
            {
                DrawStroke(pixels, width, height, stroke, frame, scale, offsetX, offsetY,
                    BridgeShell, radius + 2, 0, 0, from, to, capStart, capEnd);
            }
            if (!paint.Casing)
            {
                return;
            }
            bool tunnel = grade == MapGrade.Tunnel;
            DrawStroke(pixels, width, height, stroke, frame, scale, offsetX, offsetY,
                tunnel ? TunnelCasing : paint.CasingColor, radius + 1, tunnel ? 4 : 0, tunnel ? 2 : 0,
                from, to, capStart, capEnd);
        }

        private static void DrawRoadFill(
            Color[] pixels, int width, int height, MapStroke stroke,
            MapFrame frame, MapScale mapScale, double scale, double offsetX, double offsetY,
            MapGrade grade, int from, int to, bool capStart, bool capEnd)
        {
            StrokePaint paint = PaintForGrade(stroke.Style, grade);
            int radius = StrokePixelRadius(stroke.WidthM, scale, stroke.Style);
            DrawStroke(pixels, width, height, stroke, frame, scale, offsetX, offsetY,
                paint.Color, radius, 0, 0, from, to, capStart, capEnd);
            if (stroke.Style == MapStrokeStyle.Rail && mapScale >= MapScale.City)
            {
                DrawStroke(pixels, width, height, stroke, frame, scale, offsetX, offsetY,
                    RailDash, Math.Max(0, radius - 1), 8, 8, from, to, capStart, capEnd);
            }
        }

        private struct StrokePaint
        {
            public Color Color;
            public bool Casing;
            public Color CasingColor;
        }

        /// <summary>
        /// Palette only. Stroke width comes from world meters at raster time.
        /// </summary>
        private static StrokePaint PaintForStyle(MapStrokeStyle style)
        {
            switch (style)
            {
                case MapStrokeStyle.Highway:
                    return new StrokePaint { Color = new Color(0.910f, 0.573f, 0.635f), Casing = true, CasingColor = new Color(0.863f, 0.165f, 0.404f) };
                case MapStrokeStyle.Large:
                    return new StrokePaint { Color = new Color(0.98f, 0.76f, 0.31f), Casing = true, CasingColor = new Color(0.85f, 0.62f, 0.17f) };
                case MapStrokeStyle.Medium:
                    return new StrokePaint { Color = new Color(0.99f, 0.91f, 0.66f), Casing = true, CasingColor = new Color(0.84f, 0.77f, 0.49f) };
                case MapStrokeStyle.Rail:
                    return new StrokePaint { Color = new Color(0.439f, 0.439f, 0.439f) };
                case MapStrokeStyle.Metro:
                    return new StrokePaint { Color = new Color(0.14f, 0.14f, 0.14f), Casing = true, CasingColor = new Color(0.04f, 0.04f, 0.04f) };
                case MapStrokeStyle.Tram:
                    return new StrokePaint { Color = new Color(0.42f, 0.42f, 0.42f), Casing = true, CasingColor = new Color(0.18f, 0.18f, 0.18f) };
                default:
                    return new StrokePaint { Color = new Color(1f, 1f, 1f), Casing = true, CasingColor = new Color(0.80f, 0.78f, 0.73f) };
            }
        }

        private static StrokePaint PaintForGrade(MapStrokeStyle style, MapGrade grade)
        {
            StrokePaint paint = PaintForStyle(style);
            if (grade == MapGrade.Tunnel)
            {
                paint.Color = Color.Lerp(paint.Color, Color.white, 0.12f);
            }
            return paint;
        }

        private static Color ColorForFill(MapFillKind kind)
        {
            switch (kind)
            {
                case MapFillKind.Park:
                    return new Color(0.78f, 0.95f, 0.80f);
                case MapFillKind.Residential:
                    return new Color(0.88f, 0.87f, 0.87f);
                case MapFillKind.Commercial:
                    return new Color(0.95f, 0.85f, 0.85f);
                case MapFillKind.Industrial:
                    return new Color(0.92f, 0.86f, 0.91f);
                case MapFillKind.Office:
                    return new Color(0.93f, 0.88f, 0.82f);
                case MapFillKind.Service:
                    return new Color(0.82f, 0.80f, 0.76f);
                default:
                    return new Color(0.77f, 0.73f, 0.64f);
            }
        }

        /// <summary>
        /// Half-width in pixels: native meters at this scale, but never below
        /// the OSM Carto class floor so citywide highways stay a few pixels
        /// and rails keep a casing over water.
        /// </summary>
        private static int StrokePixelRadius(double widthM, double scale, MapStrokeStyle style)
        {
            int fromMeters = 0;
            if (widthM > 0.0 && scale > 0.0)
            {
                fromMeters = Math.Max(0, (int)Math.Round(widthM * scale * 0.5));
            }
            return Math.Max(fromMeters, MinStrokeRadius(style));
        }

        /// <summary>
        /// OSM Carto line-width at z12 is ~3.5px for motorway/primary (our
        /// radius 1). Railway is thin but still a drawn line, not zero.
        /// </summary>
        private static int MinStrokeRadius(MapStrokeStyle style)
        {
            switch (style)
            {
                case MapStrokeStyle.Highway:
                case MapStrokeStyle.Large:
                case MapStrokeStyle.Medium:
                case MapStrokeStyle.Rail:
                case MapStrokeStyle.Metro:
                case MapStrokeStyle.Tram:
                    return 1;
                default:
                    return 0;
            }
        }

        private static void DrawStroke(
            Color[] pixels, int width, int height, MapStroke stroke,
            MapFrame frame, double scale, double offsetX, double offsetY,
            Color color, int radius, int dash, int gap, int from, int to, bool capStart, bool capEnd)
        {
            if (to <= from)
            {
                return;
            }
            int stride = 1;
            int along = 0;
            for (int i = from; i < to; i += stride)
            {
                int j = Math.Min(i + stride, to);
                int x0 = (int)Math.Round((stroke.X[i] - frame.MinX) * scale + offsetX);
                int y0 = height - 1 - (int)Math.Round((stroke.Y[i] - frame.MinY) * scale + offsetY);
                int x1 = (int)Math.Round((stroke.X[j] - frame.MinX) * scale + offsetX);
                int y1 = height - 1 - (int)Math.Round((stroke.Y[j] - frame.MinY) * scale + offsetY);
                if (frame.Clip && IsOutsideFrame(x0, y0, x1, y1, width, height))
                {
                    continue;
                }
                bool start = i == from ? capStart : true;
                bool end = j == to ? capEnd : true;
                if (dash > 0)
                {
                    along = MapStrokeRaster.Dash(pixels, width, height, x0, y0, x1, y1, radius, color, dash, gap, along, start, end);
                }
                else
                {
                    MapStrokeRaster.Line(pixels, width, height, x0, y0, x1, y1, radius, color, start, end);
                }
            }
        }

        private static bool IsOutsideFrame(int x0, int y0, int x1, int y1, int width, int height)
        {
            return (x0 < 0 && x1 < 0) || (x0 >= width && x1 >= width)
                || (y0 < 0 && y1 < 0) || (y0 >= height && y1 >= height);
        }

        private static void AccumulateBounds(
            List<MapStroke> strokes,
            List<MapPolygon> fills,
            ref double minX, ref double minY, ref double maxX, ref double maxY)
        {
            foreach (MapStroke stroke in strokes)
            {
                for (int i = 0; i < stroke.X.Count; i++)
                {
                    AccumulatePoint(stroke.X[i], stroke.Y[i], ref minX, ref minY, ref maxX, ref maxY);
                }
            }
            foreach (MapPolygon polygon in fills)
            {
                for (int i = 0; i < polygon.X.Count; i++)
                {
                    AccumulatePoint(polygon.X[i], polygon.Y[i], ref minX, ref minY, ref maxX, ref maxY);
                }
            }
        }

        private static void AccumulatePoint(
            double x, double y,
            ref double minX, ref double minY, ref double maxX, ref double maxY)
        {
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y))
            {
                return;
            }
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }

        private static void FillPolygon(
            Color[] pixels, int width, int height, MapPolygon polygon,
            MapFrame frame, double scale, double offsetX, double offsetY,
            Color color)
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
                if (ys[i] < rowMin) rowMin = ys[i];
                if (ys[i] > rowMax) rowMax = ys[i];
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

        /// <summary>
        /// Diagnostic overlay for the next live run: game-meter GeoJSON of
        /// what the painter saw, plus the PNG, next to Carto's Network export.
        /// Write failures never fail the tool.
        /// </summary>
        private static void DumpMapImage(
            List<MapStroke> strokes, List<MapPolygon> fills,
            MapFrame frame, MapScale mapScale, MapLayering.Result layering, byte[] png)
        {
            try
            {
                ModPaths.EnsureDirectories();
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
                string directory = Path.Combine(ModPaths.MapImageDirectory, stamp);
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, "map.png"), png);
                File.WriteAllText(Path.Combine(directory, "meta.json"), BuildMapMeta(frame, mapScale, strokes, fills, layering));
                File.WriteAllText(Path.Combine(directory, "source.geojson"), BuildMapSourceGeoJson(strokes, fills));
                Mod.Log.Info($"map_image dump {directory}");
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"map_image dump failed: {e.GetType().Name}: {e.Message}");
            }
        }

        private static string BuildMapMeta(
            MapFrame frame, MapScale mapScale, List<MapStroke> strokes, List<MapPolygon> fills,
            MapLayering.Result layering)
        {
            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            using (var json = new JsonTextWriter(writer))
            {
                json.WriteStartObject();
                json.WritePropertyName("minX"); json.WriteValue(frame.MinX);
                json.WritePropertyName("minY"); json.WriteValue(frame.MinY);
                json.WritePropertyName("maxX"); json.WriteValue(frame.MaxX);
                json.WritePropertyName("maxY"); json.WriteValue(frame.MaxY);
                json.WritePropertyName("clip"); json.WriteValue(frame.Clip);
                json.WritePropertyName("mapScale"); json.WriteValue(mapScale.ToString());
                json.WritePropertyName("strokes"); json.WriteValue(strokes.Count);
                json.WritePropertyName("fills"); json.WriteValue(fills.Count);
                json.WritePropertyName("converged"); json.WriteValue(layering.Converged);
                json.WritePropertyName("constraints"); json.WriteValue(layering.Constraints);
                json.WritePropertyName("skippedCells"); json.WriteValue(layering.SkippedCells);
                json.WriteEndObject();
                return writer.ToString();
            }
        }

        private static string BuildMapSourceGeoJson(
            List<MapStroke> strokes, List<MapPolygon> fills)
        {
            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            using (var json = new JsonTextWriter(writer))
            {
                json.WriteStartObject();
                json.WritePropertyName("type"); json.WriteValue("FeatureCollection");
                json.WritePropertyName("features");
                json.WriteStartArray();
                foreach (MapStroke stroke in strokes)
                {
                    if (stroke.X.Count < 2)
                    {
                        continue;
                    }
                    json.WriteStartObject();
                    json.WritePropertyName("type"); json.WriteValue("Feature");
                    json.WritePropertyName("properties");
                    json.WriteStartObject();
                    json.WritePropertyName("kind"); json.WriteValue("stroke");
                    json.WritePropertyName("style"); json.WriteValue(stroke.Style.ToString());
                    json.WritePropertyName("grade"); json.WriteValue(stroke.Grade.ToString());
                    json.WritePropertyName("layer"); json.WriteValue(stroke.Layer);
                    json.WritePropertyName("widthM"); json.WriteValue(stroke.WidthM);
                    json.WritePropertyName("startNode"); json.WriteValue(stroke.StartNode);
                    json.WritePropertyName("endNode"); json.WriteValue(stroke.EndNode);
                    json.WritePropertyName("elev");
                    if (stroke.HasElev)
                    {
                        json.WriteValue(stroke.Elev);
                    }
                    else
                    {
                        json.WriteNull();
                    }
                    json.WriteEndObject();
                    json.WritePropertyName("geometry");
                    json.WriteStartObject();
                    json.WritePropertyName("type"); json.WriteValue("LineString");
                    json.WritePropertyName("coordinates");
                    json.WriteStartArray();
                    for (int i = 0; i < stroke.X.Count; i++)
                    {
                        WriteCoord(json, stroke.X[i], stroke.Y[i]);
                    }
                    json.WriteEndArray();
                    json.WriteEndObject();
                    json.WriteEndObject();
                }
                foreach (MapPolygon polygon in fills)
                {
                    if (polygon.X.Count < 3)
                    {
                        continue;
                    }
                    json.WriteStartObject();
                    json.WritePropertyName("type"); json.WriteValue("Feature");
                    json.WritePropertyName("properties");
                    json.WriteStartObject();
                    json.WritePropertyName("kind"); json.WriteValue("fill");
                    json.WritePropertyName("fillKind"); json.WriteValue(polygon.Kind.ToString());
                    json.WriteEndObject();
                    json.WritePropertyName("geometry");
                    json.WriteStartObject();
                    json.WritePropertyName("type"); json.WriteValue("Polygon");
                    json.WritePropertyName("coordinates");
                    json.WriteStartArray();
                    json.WriteStartArray();
                    for (int i = 0; i < polygon.X.Count; i++)
                    {
                        WriteCoord(json, polygon.X[i], polygon.Y[i]);
                    }
                    WriteCoord(json, polygon.X[0], polygon.Y[0]);
                    json.WriteEndArray();
                    json.WriteEndArray();
                    json.WriteEndObject();
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
                return writer.ToString();
            }
        }

        private static void WriteCoord(JsonTextWriter json, double x, double y)
        {
            json.WriteStartArray();
            json.WriteValue(x);
            json.WriteValue(y);
            json.WriteEndArray();
        }
    }
}
