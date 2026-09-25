using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace CS2MCP
{
    internal static class MapDump
    {
        internal static MapFrame Load(string directory, List<MapStroke> strokes, List<MapPolygon> fills)
        {
            using JsonDocument meta = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "meta.json")));
            JsonElement root = meta.RootElement;
            var frame = new MapFrame
            {
                MinX = root.GetProperty("minX").GetDouble(),
                MinY = root.GetProperty("minY").GetDouble(),
                MaxX = root.GetProperty("maxX").GetDouble(),
                MaxY = root.GetProperty("maxY").GetDouble(),
                Clip = root.GetProperty("clip").GetBoolean(),
            };
            using JsonDocument geo = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "source.geojson")));
            foreach (JsonElement feature in geo.RootElement.GetProperty("features").EnumerateArray())
            {
                JsonElement properties = feature.GetProperty("properties");
                JsonElement geometry = feature.GetProperty("geometry");
                string kind = properties.GetProperty("kind").GetString();
                if (kind == "stroke")
                {
                    string styleName = properties.GetProperty("style").GetString();
                    if (!Enum.TryParse(styleName, out MapStrokeStyle style))
                    {
                        continue;
                    }
                    var stroke = new MapStroke
                    {
                        Style = style,
                        Grade = Enum.Parse<MapGrade>(properties.GetProperty("grade").GetString()),
                        Layer = properties.GetProperty("layer").GetInt32(),
                        WidthM = properties.GetProperty("widthM").GetDouble(),
                    };
                    foreach (JsonElement point in geometry.GetProperty("coordinates").EnumerateArray())
                    {
                        stroke.X.Add(point[0].GetDouble());
                        stroke.Y.Add(point[1].GetDouble());
                    }
                    if (stroke.X.Count >= 2)
                    {
                        strokes.Add(stroke);
                    }
                }
                else if (kind == "fill")
                {
                    string fillName = properties.GetProperty("fillKind").GetString();
                    if (!Enum.TryParse(fillName, out MapFillKind fillKind))
                    {
                        continue;
                    }
                    var polygon = new MapPolygon
                    {
                        Kind = fillKind,
                    };
                    JsonElement ring = geometry.GetProperty("coordinates")[0];
                    foreach (JsonElement point in ring.EnumerateArray())
                    {
                        polygon.X.Add(point[0].GetDouble());
                        polygon.Y.Add(point[1].GetDouble());
                    }
                    if (polygon.X.Count >= 3)
                    {
                        fills.Add(polygon);
                    }
                }
            }
            return frame;
        }
    }

    internal static class MapPng
    {
        internal static void Write(string path, MapImageBuffer buffer)
        {
            byte[] raw = Scanlines(buffer);
            byte[] zlib;
            using (var packed = new MemoryStream())
            {
                using (var stream = new ZLibStream(packed, CompressionLevel.Fastest, leaveOpen: true))
                {
                    stream.Write(raw, 0, raw.Length);
                }
                zlib = packed.ToArray();
            }
            using var file = File.Create(path);
            file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            WriteChunk(file, "IHDR", Header(buffer.Width, buffer.Height));
            WriteChunk(file, "IDAT", zlib);
            WriteChunk(file, "IEND", Array.Empty<byte>());
        }

        private static byte[] Scanlines(MapImageBuffer buffer)
        {
            int stride = buffer.Width * 3;
            var raw = new byte[buffer.Height * (stride + 1)];
            for (int row = 0; row < buffer.Height; row++)
            {
                int dest = row * (stride + 1);
                raw[dest] = 0;
                int source = row * buffer.Width;
                for (int col = 0; col < buffer.Width; col++)
                {
                    MapRgb pixel = buffer.Pixels[source + col];
                    int at = dest + 1 + col * 3;
                    raw[at] = pixel.R;
                    raw[at + 1] = pixel.G;
                    raw[at + 2] = pixel.B;
                }
            }
            return raw;
        }

        private static byte[] Header(int width, int height)
        {
            var header = new byte[13];
            WriteInt(header, 0, width);
            WriteInt(header, 4, height);
            header[8] = 8;
            header[9] = 2;
            return header;
        }

        private static void WriteChunk(Stream file, string type, byte[] data)
        {
            var length = new byte[4];
            WriteInt(length, 0, data.Length);
            file.Write(length, 0, 4);
            byte[] name = System.Text.Encoding.ASCII.GetBytes(type);
            file.Write(name, 0, 4);
            if (data.Length > 0)
            {
                file.Write(data, 0, data.Length);
            }
            uint crc = Crc(name, data);
            var sum = new byte[4];
            WriteInt(sum, 0, (int)crc);
            file.Write(sum, 0, 4);
        }

        private static void WriteInt(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static uint Crc(byte[] type, byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            crc = Update(crc, type);
            crc = Update(crc, data);
            return crc ^ 0xFFFFFFFF;
        }

        private static uint Update(uint crc, byte[] data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i];
                for (int bit = 0; bit < 8; bit++)
                {
                    uint mask = (crc & 1) == 1 ? 0xEDB88320u : 0;
                    crc = (crc >> 1) ^ mask;
                }
            }
            return crc;
        }
    }
}
