using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentRuntime
{
    /// <summary>
    /// One assistant history message that carried reasoning text.
    /// Plain data only, so the matcher stays free of chat SDK types.
    /// </summary>
    internal sealed class ReasoningEchoSnapshot
    {
        public ReasoningEchoSnapshot(string text, string[] callIds, string reasoning)
        {
            Text = text ?? "";
            CallIds = callIds ?? new string[0];
            Reasoning = reasoning ?? "";
        }

        public string Text { get; }

        public string[] CallIds { get; }

        public string Reasoning { get; }
    }

    /// <summary>
    /// Puts DeepSeek-style <c>reasoning_content</c> back onto serialized Chat
    /// Completions assistant messages. The MEAI OpenAI adapter surfaces
    /// inbound reasoning as <c>TextReasoningContent</c> but drops it when
    /// serializing the next request, so thinking-mode gateways reject the
    /// follow-up with "reasoning_content must be passed back". Injection is a
    /// deterministic function of the stored history, which keeps the prompt
    /// prefix stable for provider caching. Messages without known reasoning
    /// are left byte-identical.
    /// </summary>
    internal static class ReasoningEchoMatcher
    {
        public const string ReasoningField = "reasoning_content";

        public static int InjectReasoning(JsonObject body, IReadOnlyList<ReasoningEchoSnapshot> history)
        {
            if (body == null || history == null || history.Count == 0)
            {
                return 0;
            }
            if (!(body["messages"] is JsonArray messages))
            {
                return 0;
            }
            int injected = 0;
            foreach (JsonNode node in messages)
            {
                if (!(node is JsonObject message))
                {
                    continue;
                }
                if (!string.Equals(GetString(message["role"]), "assistant", StringComparison.Ordinal))
                {
                    continue;
                }
                if (HasReasoning(message))
                {
                    continue;
                }
                ReasoningEchoSnapshot snapshot = FindSnapshot(message, history);
                if (snapshot == null || string.IsNullOrEmpty(snapshot.Reasoning))
                {
                    continue;
                }
                message[ReasoningField] = JsonValue.Create(snapshot.Reasoning);
                injected++;
            }
            return injected;
        }

        private static bool HasReasoning(JsonObject message)
        {
            if (!message.TryGetPropertyValue(ReasoningField, out JsonNode existing) || existing == null)
            {
                return false;
            }
            if (existing.GetValueKind() == JsonValueKind.Null)
            {
                return false;
            }
            return existing.GetValueKind() == JsonValueKind.String
                ? !string.IsNullOrEmpty(existing.GetValue<string>())
                : true;
        }

        private static ReasoningEchoSnapshot FindSnapshot(JsonObject message, IReadOnlyList<ReasoningEchoSnapshot> history)
        {
            List<string> callIds = ExtractToolCallIds(message["tool_calls"]);
            if (callIds.Count > 0)
            {
                return FindByCallIds(history, callIds);
            }
            return FindByText(history, ExtractText(message["content"]));
        }

        private static ReasoningEchoSnapshot FindByCallIds(IReadOnlyList<ReasoningEchoSnapshot> history, List<string> callIds)
        {
            foreach (ReasoningEchoSnapshot snapshot in history)
            {
                if (snapshot.CallIds.Length == 0)
                {
                    continue;
                }
                bool covers = true;
                foreach (string callId in callIds)
                {
                    if (Array.IndexOf(snapshot.CallIds, callId) < 0)
                    {
                        covers = false;
                        break;
                    }
                }
                if (covers)
                {
                    return snapshot;
                }
            }
            return null;
        }

        private static ReasoningEchoSnapshot FindByText(IReadOnlyList<ReasoningEchoSnapshot> history, string text)
        {
            foreach (ReasoningEchoSnapshot snapshot in history)
            {
                if (snapshot.CallIds.Length == 0 && string.Equals(snapshot.Text, text, StringComparison.Ordinal))
                {
                    return snapshot;
                }
            }
            foreach (ReasoningEchoSnapshot snapshot in history)
            {
                if (string.Equals(snapshot.Text, text, StringComparison.Ordinal))
                {
                    return snapshot;
                }
            }
            return null;
        }

        private static List<string> ExtractToolCallIds(JsonNode toolCalls)
        {
            var callIds = new List<string>();
            if (!(toolCalls is JsonArray calls))
            {
                return callIds;
            }
            foreach (JsonNode node in calls)
            {
                string callId = node is JsonObject call ? GetString(call["id"]) : "";
                if (callId.Length > 0)
                {
                    callIds.Add(callId);
                }
            }
            return callIds;
        }

        private static string ExtractText(JsonNode content)
        {
            if (content is JsonArray parts)
            {
                string collected = "";
                foreach (JsonNode node in parts)
                {
                    if (node is JsonObject part &&
                        string.Equals(GetString(part["type"]), "text", StringComparison.Ordinal))
                    {
                        collected += GetString(part["text"]);
                    }
                }
                return collected;
            }
            return GetString(content);
        }

        private static string GetString(JsonNode node)
        {
            if (node is JsonValue value && value.TryGetValue<string>(out string text))
            {
                return text ?? "";
            }
            return "";
        }
    }
}
