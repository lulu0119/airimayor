using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace airimayor.Host
{
    /// <summary>
    /// One building, road, pipe, cable, or ground point the player marked
    /// for the chat. The city does not draw it.
    /// </summary>
    public sealed class PointedPlace
    {
        public string Id;
        public string Kind;
        public string Name;
        public float X;
        public float Z;
        public int Index;
        public int Version;
        public float EndX;
        public float EndZ;
        public bool HasEnd;
    }

    public static class PointedPlaceText
    {
        public const int Maximum = 8;

        public static string Compose(string playerText, IReadOnlyList<PointedPlace> places)
        {
            string words = (playerText ?? "").Trim();
            string appendix = Appendix(places);
            if (appendix.Length == 0)
            {
                return words;
            }
            if (words.Length == 0)
            {
                return appendix;
            }
            return words + "\n\n" + appendix;
        }

        public static string Appendix(IReadOnlyList<PointedPlace> places)
        {
            if (places == null || places.Count == 0)
            {
                return "";
            }
            var text = new StringBuilder();
            text.Append("Pointed at:");
            for (int i = 0; i < places.Count; i++)
            {
                text.Append('\n');
                text.Append("- ");
                text.Append(Line(places[i]));
            }
            return text.ToString();
        }

        public static string ToJson(IReadOnlyList<PointedPlace> places)
        {
            var array = new JsonArray();
            if (places != null)
            {
                for (int i = 0; i < places.Count; i++)
                {
                    array.Add(ToNode(places[i]));
                }
            }
            return array.ToJsonString();
        }

        public static bool TryParse(string json, out PointedPlace place)
        {
            place = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }
            JsonNode node;
            try
            {
                node = JsonNode.Parse(json);
            }
            catch (System.Text.Json.JsonException)
            {
                return false;
            }
            return TryRead(node as JsonObject, out place);
        }

        private static string Line(PointedPlace place)
        {
            if (place == null)
            {
                return "";
            }
            string name = string.IsNullOrWhiteSpace(place.Name) ? "Place" : place.Name.Trim();
            if (place.Kind == "point")
            {
                return name + " at (" + Meters(place.X) + ", " + Meters(place.Z) + ") m";
            }
            if (place.HasEnd)
            {
                return name
                    + " from (" + Meters(place.X) + ", " + Meters(place.Z) + ")"
                    + " to (" + Meters(place.EndX) + ", " + Meters(place.EndZ) + ") m ("
                    + place.Kind + " " + place.Index.ToString(CultureInfo.InvariantCulture)
                    + "." + place.Version.ToString(CultureInfo.InvariantCulture) + ")";
            }
            return name
                + " at (" + Meters(place.X) + ", " + Meters(place.Z) + ") m ("
                + place.Kind + " " + place.Index.ToString(CultureInfo.InvariantCulture)
                + "." + place.Version.ToString(CultureInfo.InvariantCulture) + ")";
        }

        private static string Meters(float value)
        {
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        }

        private static JsonObject ToNode(PointedPlace place)
        {
            var node = new JsonObject
            {
                ["id"] = place.Id ?? "",
                ["kind"] = place.Kind ?? "",
                ["name"] = place.Name ?? "",
                ["x"] = place.X,
                ["z"] = place.Z,
                ["index"] = place.Index,
                ["version"] = place.Version,
            };
            if (place.HasEnd)
            {
                node["endX"] = place.EndX;
                node["endZ"] = place.EndZ;
            }
            return node;
        }

        private static bool TryRead(JsonObject node, out PointedPlace place)
        {
            place = null;
            if (node == null)
            {
                return false;
            }
            string kind = TryString(node, "kind");
            string name = TryString(node, "name");
            if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(name))
            {
                return false;
            }
            if (!TryFloat(node, "x", out float x) || !TryFloat(node, "z", out float z))
            {
                return false;
            }
            place = new PointedPlace
            {
                Id = TryString(node, "id") ?? "",
                Kind = kind,
                Name = name,
                X = x,
                Z = z,
                Index = TryInt(node, "index"),
                Version = TryInt(node, "version"),
            };
            if (TryFloat(node, "endX", out float endX) && TryFloat(node, "endZ", out float endZ))
            {
                place.HasEnd = true;
                place.EndX = endX;
                place.EndZ = endZ;
            }
            return true;
        }

        private static bool TryFloat(JsonObject node, string name, out float value)
        {
            value = 0f;
            if (!TryNumber(node[name], out double number))
            {
                return false;
            }
            value = (float)number;
            return true;
        }

        private static int TryInt(JsonObject node, string name)
        {
            if (!TryNumber(node[name], out double number))
            {
                return 0;
            }
            return (int)Math.Round(number);
        }

        private static string TryString(JsonObject node, string name)
        {
            if (node[name] is JsonValue value && value.TryGetValue(out string parsed))
            {
                return parsed;
            }
            return null;
        }

        private static bool TryNumber(JsonNode child, out double number)
        {
            number = 0d;
            if (!(child is JsonValue value))
            {
                return false;
            }
            if (value.TryGetValue(out double parsed))
            {
                number = parsed;
                return true;
            }
            return false;
        }
    }
}
