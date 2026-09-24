using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentRuntime
{
    /// <summary>
    /// Loop-owned plan. The model declares it with set_plan.
    /// Player messages and autonomous continuation leave it in place.
    /// </summary>
    internal sealed class SessionPlan
    {
        public const string ToolName = "set_plan";

        public const string HistoryNotePrefix = "[active plan] ";

        public const string ToolDescription =
            "Declare the single active plan. " +
            "goal is one sentence. success is the measurable stop condition. " +
            "Replaces the previous plan.";

        private static readonly JsonDocument ParametersDocument = JsonDocument.Parse(
            @"{
                ""type"": ""object"",
                ""properties"": {
                    ""goal"": {
                        ""type"": ""string"",
                        ""description"": ""One sentence: what this plan will do""
                    },
                    ""success"": {
                        ""type"": ""string"",
                        ""description"": ""Measurable stop condition""
                    }
                },
                ""required"": [""goal"", ""success""]
            }");

        public static JsonElement Parameters => ParametersDocument.RootElement;

        private readonly object m_Gate = new object();
        private string m_Goal;
        private string m_Success;

        public PlanCallResult SetPlan(string argumentsJson)
        {
            if (!TryReadField(argumentsJson, "goal", out string goal))
            {
                return PlanCallResult.Fail("goal is required");
            }
            if (!TryReadField(argumentsJson, "success", out string success))
            {
                return PlanCallResult.Fail("success is required");
            }

            lock (m_Gate)
            {
                m_Goal = goal;
                m_Success = success;
                return PlanCallResult.Ok(ToUiJsonUnlocked());
            }
        }

        public void Clear()
        {
            lock (m_Gate)
            {
                m_Goal = null;
                m_Success = null;
            }
        }

        /// <summary>
        /// Pinned system note for every model round. Always starts with
        /// <see cref="HistoryNotePrefix"/>.
        /// </summary>
        public string LiveNote()
        {
            lock (m_Gate)
            {
                if (string.IsNullOrEmpty(m_Goal))
                {
                    return HistoryNotePrefix +
                        "none. Call set_plan when you can name one goal.";
                }

                return HistoryNotePrefix + "goal=" + m_Goal +
                    " success=" + m_Success +
                    ". Continue this plan. Do not start a new review until this plan is met or you call set_plan to replace it.";
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
            if (string.IsNullOrEmpty(m_Goal))
            {
                return "";
            }
            return new JsonObject
            {
                ["goal"] = m_Goal,
                ["success"] = m_Success,
            }.ToJsonString();
        }

        private static bool TryReadField(string argumentsJson, string name, out string value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                return false;
            }
            try
            {
                using (JsonDocument document = JsonDocument.Parse(argumentsJson))
                {
                    JsonElement root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return false;
                    }
                    foreach (JsonProperty property in root.EnumerateObject())
                    {
                        if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            value = property.Value.GetString();
                        }
                        else if (property.Value.ValueKind != JsonValueKind.Null &&
                            property.Value.ValueKind != JsonValueKind.Undefined)
                        {
                            value = property.Value.ToString();
                        }
                        return !string.IsNullOrWhiteSpace(value);
                    }
                }
            }
            catch (JsonException)
            {
                return false;
            }
            return false;
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
