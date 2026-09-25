using System;

namespace CS2MCP
{
    internal struct MapRgb : IEquatable<MapRgb>
    {
        public byte R;
        public byte G;
        public byte B;

        public MapRgb(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        public static MapRgb FromUnit(float r, float g, float b)
        {
            return new MapRgb(ToByte(r), ToByte(g), ToByte(b));
        }

        public static MapRgb Lerp(MapRgb from, MapRgb to, float t)
        {
            return new MapRgb(
                (byte)Math.Round(from.R + (to.R - from.R) * t),
                (byte)Math.Round(from.G + (to.G - from.G) * t),
                (byte)Math.Round(from.B + (to.B - from.B) * t));
        }

        public bool Equals(MapRgb other)
        {
            return R == other.R && G == other.G && B == other.B;
        }

        public override bool Equals(object obj)
        {
            return obj is MapRgb other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (R << 16) | (G << 8) | B;
        }

        private static byte ToByte(float unit)
        {
            return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(unit * 255f)));
        }
    }
}
