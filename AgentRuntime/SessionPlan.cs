using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentRuntime
{
    /// <summary>
    /// Loop-owned plan: one list of steps. The model replaces the whole list
    /// with set_plan. Player messages and autonomous continuation leave it in place.
    /// </summary>
    internal sealed class SessionPlan
    {
        public const string ToolName = "set_plan";

        public const string ToolDescription =
            "Declare the single active plan as a list of steps. " +
            "Each entry has content, priority (high, medium, or low), " +
            "and status (pending, in_progress, or completed). " +
            "Replaces the previous plan.";

        private static readonly JsonDocument ParametersDocument = JsonDocument.Parse(
            @"{
                ""type"": ""object"",
                ""properties"": {
                    ""entries"": {
                        ""type"": ""array"",
                        ""description"": ""The complete step list. Replaces the previous plan."",
                        ""items"": {
                            ""type"": ""object"",
                            ""properties"": {
                                ""content"": {
                                    ""type"": ""string"",
                                    ""description"": ""What this step will do""
                                },
                                ""priority"": {
                                    ""type"": ""string"",
                                    ""enum"": [""high"", ""medium"", ""low""],
                                    ""description"": ""high, medium, or low""
                                },
                                ""status"": {
                                    ""type"": ""string"",
                                    ""enum"": [""pending"", ""in_progress"", ""completed""],
                                    ""description"": ""pending, in_progress, or completed""
                                }
                            },
                            ""required"": [""content"", ""priority"", ""status""]
                        }
                    }
                },
                ""required"": [""entries""]
            }");

        public static JsonElement Parameters => ParametersDocument.RootElement;

        private readonly object m_Gate = new object();
        private readonly List<PlanEntry> m_Entries = new List<PlanEntry>();

        public PlanCallResult SetPlan(string argumentsJson)
        {
            if (!TryReadEntries(argumentsJson, out List<PlanEntry> entries, out string error))
            {
                return PlanCallResult.Fail(error);
            }

            lock (m_Gate)
            {
                m_Entries.Clear();
                m_Entries.AddRange(entries);
                return PlanCallResult.Ok(ToUiJsonUnlocked());
            }
        }

        public void Clear()
        {
            lock (m_Gate)
            {
                m_Entries.Clear();
            }
        }

        public string ToUiJson()
        {
            lock (m_Gate)
            {
                return ToUiJsonUnlocked();
            }
        }

        private string ToUiJsonUnlocked()
        {
            if (m_Entries.Count == 0)
            {
                return "";
            }

            var entries = new JsonArray();
            foreach (PlanEntry entry in m_Entries)
            {
                entries.Add(new JsonObject
                {
                    ["content"] = entry.Content,
                    ["priority"] = entry.Priority,
                    ["status"] = entry.Status,
                });
            }
            return new JsonObject
            {
                ["entries"] = entries,
            }.ToJsonString();
        }

        private static bool TryReadEntries(string argumentsJson, out List<PlanEntry> entries, out string error)
        {
            entries = null;
            error = "entries is required";
            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                return false;
            }

            try
            {
                using (JsonDocument document = JsonDocument.Parse(argumentsJson))
                {
                    JsonElement root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object ||
                        !TryGetProperty(root, "entries", out JsonElement rawEntries) ||
                        rawEntries.ValueKind != JsonValueKind.Array)
                    {
                        return false;
                    }
                    if (rawEntries.GetArrayLength() == 0)
                    {
                        error = "entries must contain one step";
                        return false;
                    }

                    var parsed = new List<PlanEntry>();
                    foreach (JsonElement item in rawEntries.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object ||
                            !TryGetString(item, "content", out string content))
                        {
                            error = "content is required";
                            return false;
                        }
                        if (!TryGetString(item, "priority", out string priority) ||
                            (priority != "high" && priority != "medium" && priority != "low"))
                        {
                            error = "priority must be high, medium, or low";
                            return false;
                        }
                        if (!TryGetString(item, "status", out string status) ||
                            (status != "pending" && status != "in_progress" && status != "completed"))
                        {
                            error = "status must be pending, in_progress, or completed";
                            return false;
                        }
                        parsed.Add(new PlanEntry(content, priority, status));
                    }

                    entries = parsed;
                    error = null;
                    return true;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
        {
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
            value = default(JsonElement);
            return false;
        }

        private static bool TryGetString(JsonElement item, string name, out string value)
        {
            value = null;
            if (!TryGetProperty(item, name, out JsonElement raw) ||
                raw.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            value = raw.GetString();
            return !string.IsNullOrWhiteSpace(value);
        }

        private readonly struct PlanEntry
        {
            public PlanEntry(string content, string priority, string status)
            {
                Content = content;
                Priority = priority;
                Status = status;
            }

            public string Content { get; }
            public string Priority { get; }
            public string Status { get; }
        }
    }

    internal readonly struct PlanCallResult
    {
        public readonly bool Success;
        public readonly string Text;

        private PlanCallResult(bool success, string text)
        {
            Success = success;
            Text = text;
        }

        public static PlanCallResult Ok(string text)
        {
            return new PlanCallResult(true, text);
        }

        public static PlanCallResult Fail(string message)
        {
            return new PlanCallResult(
                false,
                JsonSerializer.Serialize(new { error = message }));
        }
    }
}
