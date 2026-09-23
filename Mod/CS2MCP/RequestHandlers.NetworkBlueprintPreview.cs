using System;
using System.Collections.Generic;
using Game.Simulation;
using Unity.Mathematics;
using UnityEngine;

namespace CS2MCP
{
    /// <summary>
    /// Blueprint review rendering. The plan overlay draws the exact resolved
    /// courses that construction will consume; the profile draws terrain
    /// against road height for the longest structured course.
    /// </summary>
    public sealed partial class RequestHandlers
    {
        private static readonly Color BlueprintGround = new Color(1f, 0.45f, 0.10f);
        private static readonly Color BlueprintCasing = new Color(0.45f, 0.16f, 0.03f);
        private static readonly Color BlueprintElevated = new Color(0.95f, 0.15f, 0.25f);
        private static readonly Color BlueprintTunnel = new Color(0.75f, 0.75f, 0.75f);

        private bool TryGetPreviewBlueprint(
            BridgeRequest request, out BlueprintRecord record, out BridgeResponse error)
        {
            record = null;
            error = null;
            if (!request.Query.TryGetValue("blueprint", out string blueprintId) || string.IsNullOrEmpty(blueprintId))
            {
                return false;
            }
            int version = request.TryGetInt("version", out int rawVersion) ? rawVersion : 0;
            record = NetworkBlueprintStore.Get(blueprintId, version);
            if (record == null)
            {
                error = BridgeResponse.Error(BridgeErrorKind.NotFound, "unknown blueprint revision; plan the sketch again");
                return false;
            }
            return true;
        }

        private MapFrame BlueprintFrame(BlueprintRecord record)
        {
            double minX = double.PositiveInfinity;
            double minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity;
            double maxY = double.NegativeInfinity;
            foreach (ResolvedCourse course in record.Courses)
            {
                List<float2> center = NetworkBlueprintPlanner.SampleCenter(course.Path);
                foreach (float2 point in center)
                {
                    if (point.x < minX) minX = point.x;
                    if (point.y < minY) minY = point.y;
                    if (point.x > maxX) maxX = point.x;
                    if (point.y > maxY) maxY = point.y;
                }
            }
            if (!(maxX > minX) || !(maxY > minY))
            {
                return null;
            }
            const double pad = 100.0;
            return new MapFrame
            {
                MinX = minX - pad,
                MinY = minY - pad,
                MaxX = maxX + pad,
                MaxY = maxY + pad,
                Clip = true,
            };
        }

        private void DrawBlueprintOverlay(Texture2D texture, BlueprintRecord record, MapFrame frame)
        {
            int width = texture.width;
            int height = texture.height;
            double spanX = frame.MaxX - frame.MinX;
            double spanY = frame.MaxY - frame.MinY;
            double scale = Math.Min(width / spanX, height / spanY);
            double offsetX = (width - spanX * scale) * 0.5;
            double offsetY = (height - spanY * scale) * 0.5;
            TerrainSystem terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            TerrainHeightData heights = terrain.GetHeightData();
            Color[] pixels = texture.GetPixels();
            foreach (ResolvedCourse course in record.Courses)
            {
                List<float2> center = NetworkBlueprintPlanner.SampleCenter(course.Path);
                var xs = new List<int>(center.Count);
                var ys = new List<int>(center.Count);
                foreach (float2 point in center)
                {
                    xs.Add((int)Math.Round((point.x - frame.MinX) * scale + offsetX));
                    ys.Add(height - 1 - (int)Math.Round((point.y - frame.MinY) * scale + offsetY));
                }
                bool buried = CourseIsBuried(course, heights);
                bool elevated = !buried && CourseIsElevated(course, heights);
                int radius = Math.Max(StrokePixelRadius(course.WidthM > 0 ? course.WidthM : 8.0, scale, MapStrokeStyle.Medium), 1);
                Color color = buried ? BlueprintTunnel : elevated ? BlueprintElevated : BlueprintGround;
                if (!buried && radius >= 2)
                {
                    StrokePolyline(pixels, width, height, xs, ys, BlueprintCasing, radius + 1, 0, 0);
                }
                if (buried)
                {
                    StrokePolyline(pixels, width, height, xs, ys, color, radius, 6, 4);
                }
                else
                {
                    StrokePolyline(pixels, width, height, xs, ys, color, radius, 0, 0);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
        }

        private static void StrokePolyline(
            Color[] pixels, int width, int height, List<int> xs, List<int> ys,
            Color color, int radius, int dash, int gap)
        {
            int along = 0;
            for (int i = 0; i + 1 < xs.Count; i++)
            {
                if (dash > 0)
                {
                    along = MapStrokeRaster.Dash(pixels, width, height, xs[i], ys[i], xs[i + 1], ys[i + 1],
                        radius, color, dash, gap, along, i == 0, i + 2 == xs.Count);
                }
                else
                {
                    MapStrokeRaster.Line(pixels, width, height, xs[i], ys[i], xs[i + 1], ys[i + 1],
                        radius, color, i == 0, i + 2 == xs.Count);
                }
            }
        }

        private static bool CourseIsBuried(ResolvedCourse course, TerrainHeightData heights)
        {
            int buried = 0;
            int total = 0;
            for (int i = 0; i <= 16; i++)
            {
                float3 point = NetworkBlueprintPlanner.CubicPoint3(course.Path, i / 16f);
                float terrain = TerrainUtils.SampleHeight(ref heights, new float3(point.x, 0f, point.z));
                total++;
                if (point.y < terrain - 1.5f)
                {
                    buried++;
                }
            }
            return buried * 2 >= total;
        }

        private static bool CourseIsElevated(ResolvedCourse course, TerrainHeightData heights)
        {
            for (int i = 0; i <= 16; i++)
            {
                float3 point = NetworkBlueprintPlanner.CubicPoint3(course.Path, i / 16f);
                float terrain = TerrainUtils.SampleHeight(ref heights, new float3(point.x, 0f, point.z));
                if (point.y > terrain + 2.5f)
                {
                    return true;
                }
            }
            return false;
        }

        private BridgeResponse RenderBlueprintProfile(BlueprintRecord record)
        {
            ResolvedCourse course = null;
            foreach (ResolvedCourse candidate in record.Courses)
            {
                if (course == null)
                {
                    course = candidate;
                    continue;
                }
                bool candidateStructured = candidate.Mode == RoadBuildMode.GradeSeparated;
                bool currentStructured = course.Mode == RoadBuildMode.GradeSeparated;
                if ((candidateStructured && (!currentStructured || candidate.Length > course.Length))
                    || (!candidateStructured && !currentStructured && candidate.Length > course.Length))
                {
                    course = candidate;
                }
            }
            if (course == null)
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict, "blueprint has no roads to section");
            }
            const int width = 1280;
            const int height = 640;
            const int padLeft = 60;
            const int padRight = 30;
            const int padTop = 30;
            const int padBottom = 50;
            TerrainSystem terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            TerrainHeightData heights = terrain.GetHeightData();
            const int samples = 128;
            var roadY = new float[samples + 1];
            var groundY = new float[samples + 1];
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                float3 point = NetworkBlueprintPlanner.CubicPoint3(course.Path, t);
                float ground = TerrainUtils.SampleHeight(ref heights, new float3(point.x, 0f, point.z));
                roadY[i] = point.y;
                groundY[i] = ground;
                if (point.y < minY) minY = point.y;
                if (point.y > maxY) maxY = point.y;
                if (ground < minY) minY = ground;
                if (ground > maxY) maxY = ground;
            }
            if (maxY - minY < 2f)
            {
                float mid = (maxY + minY) * 0.5f;
                minY = mid - 1f;
                maxY = mid + 1f;
            }
            var pixels = new Color[width * height];
            var background = new Color(0.96f, 0.95f, 0.90f);
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = background;
            }
            var axis = new Color(0.55f, 0.55f, 0.55f);
            var terrainLine = new Color(0.55f, 0.40f, 0.25f);
            for (int i = 0; i <= samples; i++)
            {
                int x0 = padLeft + (width - padLeft - padRight) * i / samples;
                int gy = height - padBottom - (int)((groundY[i] - minY) / (maxY - minY) * (height - padTop - padBottom));
                int ry = height - padBottom - (int)((roadY[i] - minY) / (maxY - minY) * (height - padTop - padBottom));
                if (i > 0)
                {
                    int px = padLeft + (width - padLeft - padRight) * (i - 1) / samples;
                    int pgy = height - padBottom - (int)((groundY[i - 1] - minY) / (maxY - minY) * (height - padTop - padBottom));
                    int pry = height - padBottom - (int)((roadY[i - 1] - minY) / (maxY - minY) * (height - padTop - padBottom));
                    MapStrokeRaster.Line(pixels, width, height, px, pgy, x0, gy, 2, terrainLine, i == 1, i == samples);
                    MapStrokeRaster.Line(pixels, width, height, px, pry, x0, ry, 3, BlueprintGround, i == 1, i == samples);
                }
            }
            MapStrokeRaster.Line(pixels, width, height, padLeft, padTop, padLeft, height - padBottom, 1, axis, true, true);
            MapStrokeRaster.Line(pixels, width, height, padLeft, height - padBottom, width - padRight, height - padBottom, 1, axis, true, true);
            Texture2D texture = null;
            try
            {
                texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.SetPixels(pixels);
                texture.Apply();
                byte[] png = ImageConversion.EncodeToPNG(texture);
                if (png == null || png.Length == 0)
                {
                    return BridgeResponse.Error(BridgeErrorKind.Internal, "profile PNG encode failed");
                }
                return BridgeResponse.Png(png,
                    ToolPreview.EncodeThumbnail(texture, out int previewWidth, out int previewHeight),
                    previewWidth, previewHeight);
            }
            finally
            {
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }
        }
    }
}
