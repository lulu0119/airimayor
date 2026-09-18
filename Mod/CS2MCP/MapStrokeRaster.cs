using System;
using UnityEngine;

namespace CS2MCP
{
    /// <summary>
    /// OSM Carto constant-width lines: a disk stamp along the polyline gives
    /// round joins. Caps are round at true ends and butt at grade splits so
    /// a ramp does not grow a node where ground becomes bridge.
    /// </summary>
    internal static class MapStrokeRaster
    {
        public static void Line(
            Color[] pixels, int width, int height, int x0, int y0, int x1, int y1, int radius, Color color,
            bool capStart, bool capEnd)
        {
            Walk(pixels, width, height, x0, y0, x1, y1, radius, color, 0, 0, 0, capStart, capEnd);
        }

        public static int Dash(
            Color[] pixels, int width, int height, int x0, int y0, int x1, int y1, int radius, Color color,
            int dash, int gap, int along, bool capStart, bool capEnd)
        {
            return Walk(pixels, width, height, x0, y0, x1, y1, radius, color, dash, gap, along, capStart, capEnd);
        }

        private static int Walk(
            Color[] pixels, int width, int height, int x0, int y0, int x1, int y1, int radius, Color color,
            int dash, int gap, int along, bool capStart, bool capEnd)
        {
            int dx = x1 - x0;
            int dy = y1 - y0;
            int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
            int period = dash > 0 ? dash + Math.Max(0, gap) : 0;
            if (steps == 0)
            {
                if ((capStart || capEnd) && (period == 0 || along % period < dash))
                {
                    Stamp(pixels, width, height, x0, y0, radius, color, 0, 0, false);
                }
                return along + 1;
            }
            for (int i = 0; i <= steps; i++)
            {
                int x = x0 + dx * i / steps;
                int y = y0 + dy * i / steps;
                if (period == 0 || along % period < dash)
                {
                    bool clip = (i == 0 && !capStart) || (i == steps && !capEnd);
                    int hx = i == steps ? -dx : dx;
                    int hy = i == steps ? -dy : dy;
                    Stamp(pixels, width, height, x, y, radius, color, hx, hy, clip);
                }
                along++;
            }
            return along;
        }

        private static void Stamp(
            Color[] pixels, int width, int height, int x, int y, int radius, Color color,
            int hx, int hy, bool clip)
        {
            if (radius <= 0)
            {
                if ((uint)x < (uint)width && (uint)y < (uint)height)
                {
                    pixels[y * width + x] = color;
                }
                return;
            }
            int r2 = radius * radius;
            int y0 = Math.Max(0, y - radius);
            int y1 = Math.Min(height - 1, y + radius);
            int x0 = Math.Max(0, x - radius);
            int x1 = Math.Min(width - 1, x + radius);
            for (int py = y0; py <= y1; py++)
            {
                int ddy = py - y;
                int row = py * width;
                for (int px = x0; px <= x1; px++)
                {
                    int ddx = px - x;
                    if (ddx * ddx + ddy * ddy > r2)
                    {
                        continue;
                    }
                    if (clip && (long)ddx * hx + (long)ddy * hy < 0)
                    {
                        continue;
                    }
                    pixels[row + px] = color;
                }
            }
        }
    }
}
