using System;
using System.Collections.Generic;

namespace CS2MCP
{
    internal enum MapScale
    {
        Region,
        City,
        District,
        Site,
    }

    internal enum MapStrokeStyle
    {
        Minor,
        Medium,
        Large,
        Highway,
        Rail,
        Metro,
        Tram,
    }

    internal enum MapGrade
    {
        Tunnel,
        Ground,
        Bridge,
    }

    internal enum MapFillKind
    {
        Building,
        Residential,
        Commercial,
        Industrial,
        Office,
        Park,
        Service,
    }

    internal sealed class MapStroke
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
    }

    internal sealed class MapPolygon
    {
        public MapFillKind Kind;
        public readonly List<double> X = new List<double>();
        public readonly List<double> Y = new List<double>();
    }

    internal sealed class MapFrame
    {
        public const double WorldHalfM = 7168.0;

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
            foreach (MapStroke stroke in strokes)
            {
                for (int i = 0; i < stroke.X.Count; i++)
                {
                    Accumulate(stroke.X[i], stroke.Y[i], ref minX, ref minY, ref maxX, ref maxY);
                }
            }
            foreach (MapPolygon polygon in fills)
            {
                for (int i = 0; i < polygon.X.Count; i++)
                {
                    Accumulate(polygon.X[i], polygon.Y[i], ref minX, ref minY, ref maxX, ref maxY);
                }
            }
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
                MinX = -WorldHalfM,
                MinY = -WorldHalfM,
                MaxX = WorldHalfM,
                MaxY = WorldHalfM,
            };
        }

        private static void Accumulate(
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
    }

    internal sealed class MapImageBuffer
    {
        public int Width;
        public int Height;
        public MapRgb[] Pixels;
    }
}
