using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CS2MCP
{
    /// <summary>
    /// Builds the model-facing wait_simulation result: wait mechanics only
    /// (hours/completed/targetReached/note). Deliberately no city snapshot;
    /// the model reads overview and problems through the read tools.
    /// </summary>
    internal static class SimWaitResult
    {
        private const string NoteTimeout =
            "wait did not finish in time; retry wait_simulation once";
        private const string NoteAborted =
            "wait aborted: simulation did not advance (game paused or a modal overlay is open)";
        private const string NoteFinished =
            "wait finished; simulation restored to its previous speed/pause state";

        public static string Build(
            string waitJson,
            string stateJson,
            bool completed)
        {
            JsonObject waitRoot = ParseObject(waitJson);
            bool targetReached = TargetReached(waitRoot, ParseObject(stateJson));
            var result = new JsonObject
            {
                ["hours"] = CopyHours(waitRoot),
                ["completed"] = completed,
                ["targetReached"] = targetReached,
                ["note"] = Note(completed, targetReached),
            };
            return result.ToJsonString();
        }

        private static JsonNode CopyHours(JsonObject waitRoot)
        {
            JsonNode hours = waitRoot != null ? waitRoot["hours"] : null;
            return hours != null ? hours.DeepClone() : JsonValue.Create(1);
        }

        private static bool TargetReached(JsonObject waitRoot, JsonObject stateRoot)
        {
            JsonNode targetNode = waitRoot != null ? waitRoot["targetFrame"] : null;
            JsonNode frameNode = stateRoot != null ? stateRoot["simulation"]?["frameIndex"] : null;
            if (targetNode == null || frameNode == null)
            {
                return false;
            }
            if (!long.TryParse(
                    targetNode.ToString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long targetFrame))
            {
                return false;
            }
            return long.TryParse(
                    frameNode.ToString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long finalFrame) &&
                finalFrame >= targetFrame;
        }

        private static string Note(bool completed, bool targetReached)
        {
            if (!completed)
            {
                return NoteTimeout;
            }
            return targetReached ? NoteFinished : NoteAborted;
        }

        private static JsonObject ParseObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            try
            {
                return JsonNode.Parse(json) as JsonObject;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
