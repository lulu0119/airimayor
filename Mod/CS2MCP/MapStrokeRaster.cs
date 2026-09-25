using System;

namespace CS2MCP
{
    /// <summary>
    /// Disk stamps along the polyline. A cap flag draws the round end;
    /// without it the end is cut flat. A pattern is on, off, on, off in
    /// pixels. A zero-length on draws nothing.
    /// </summary>
    internal static class MapStrokeRaster
    {
        public static void Line(
            MapRgb[] pixels, int width, int height, int x0, int y0, int x1, int y1, int radius, MapRgb color,
            bool capStart, bool capEnd)
        {
            Walk(pixels, width, height, x0, y0, x1, y1, radius, color, null, 0, capStart, capEnd);
        }

        public static int Pattern(
            MapRgb[] pixels, int width, int height, int x0, int y0, int x1, int y1, int radius, MapRgb color,
            int[] pattern, int along, bool capStart, bool capEnd)
        {
            return Walk(pixels, width, height, x0, y0, x1, y1, radius, color, pattern, along, capStart, capEnd);
        }

        private static int Walk(
            MapRgb[] pixels, int width, int height, int x0, int y0, int x1, int y1, int radius, MapRgb color,
            int[] pattern, int along, bool capStart, bool capEnd)
        {
            int dx = x1 - x0;
            int dy = y1 - y0;
            int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
            int period = Period(pattern);
            if (steps == 0)
            {
                if ((capStart || capEnd) && Ink(pattern, period, along))
                {
                    Stamp(pixels, width, height, x0, y0, radius, color, false, 0, 0, 0, 0, false, 0, 0, 0, 0);
                }
                return along + 1;
            }
            for (int i = 0; i <= steps; i++)
            {
                int x = x0 + dx * i / steps;
                int y = y0 + dy * i / steps;
                if (Ink(pattern, period, along))
                {
                    Stamp(
                        pixels, width, height, x, y, radius, color,
                        !capStart, x0, y0, dx, dy,
                        !capEnd, x1, y1, -dx, -dy);
                }
                along++;
            }
            return along;
        }

        private static int Period(int[] pattern)
        {
            if (pattern == null || pattern.Length == 0)
            {
                return 0;
            }
            int period = 0;
            for (int i = 0; i < pattern.Length; i++)
            {
                period += Math.Max(0, pattern[i]);
            }
            return period;
        }

        private static bool Ink(int[] pattern, int period, int along)
        {
            if (period <= 0)
            {
                return true;
            }
            int t = along % period;
            if (t < 0)
            {
                t += period;
            }
            for (int i = 0; i < pattern.Length; i++)
            {
                int len = Math.Max(0, pattern[i]);
                bool on = (i % 2) == 0;
                if (t < len)
                {
                    return on && len > 0;
                }
                t -= len;
            }
            return false;
        }

        private static void Stamp(
            MapRgb[] pixels, int width, int height, int x, int y, int radius, MapRgb color,
            bool clipStart, int startX, int startY, int startHx, int startHy,
            bool clipEnd, int endX, int endY, int endHx, int endHy)
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
                    if (clipStart && (long)(px - startX) * startHx + (long)(py - startY) * startHy < 0)
                    {
                        continue;
                    }
                    if (clipEnd && (long)(px - endX) * endHx + (long)(py - endY) * endHy < 0)
                    {
                        continue;
                    }
                    pixels[row + px] = color;
                }
            }
        }
    }
}
