using System.Collections.Generic;
using System.Text.Json.Nodes;
using AgentRuntime;
using Xunit;

namespace airimayor.Agent
{
    public sealed class ReasoningEchoMatcherTests
    {
        [Fact]
        public void Injects_reasoning_into_text_assistant_by_exact_text()
        {
            JsonObject body = Parse(@"{
                ""model"": ""deepseek-v4.1-flash"",
                ""messages"": [
                    { ""role"": ""system"", ""content"": ""Be a mayor."" },
                    { ""role"": ""user"", ""content"": ""Keep building."" },
                    { ""role"": ""assistant"", ""content"": ""City summary: 34k pop."" },
                    { ""role"": ""system"", ""content"": ""Autonomous continuation."" }
                ]
            }");
            var history = new List<ReasoningEchoSnapshot>
            {
                new ReasoningEchoSnapshot("City summary: 34k pop.", new string[0], "Unemployment down to 14.8%."),
            };

            int injected = ReasoningEchoMatcher.InjectReasoning(body, history);

            Assert.Equal(1, injected);
            JsonArray messages = body["messages"] as JsonArray;
            Assert.Equal("Unemployment down to 14.8%.", messages[2]["reasoning_content"]?.GetValue<string>());
            Assert.Null(messages[0]["reasoning_content"]);
            Assert.Null(messages[1]["reasoning_content"]);
            Assert.Null(messages[3]["reasoning_content"]);
        }

        [Fact]
        public void Injects_reasoning_into_tool_call_assistant_by_call_ids()
        {
            JsonObject body = Parse(@"{
                ""messages"": [
                    { ""role"": ""assistant"", ""content"": null, ""tool_calls"": [
                        { ""id"": ""call_1"", ""type"": ""function"", ""function"": { ""name"": ""labor"", ""arguments"": ""{}"" } },
                        { ""id"": ""call_2"", ""type"": ""function"", ""function"": { ""name"": ""budget"", ""arguments"": ""{}"" } }
                    ] },
                    { ""role"": ""tool"", ""tool_call_id"": ""call_1"", ""content"": ""{}"" }
                ]
            }");
            var history = new List<ReasoningEchoSnapshot>
            {
                new ReasoningEchoSnapshot("", new[] { "call_1", "call_2" }, "Check labor then budget."),
            };

            int injected = ReasoningEchoMatcher.InjectReasoning(body, history);

            Assert.Equal(1, injected);
            Assert.Equal("Check labor then budget.", (body["messages"] as JsonArray)[0]["reasoning_content"]?.GetValue<string>());
        }

        [Fact]
        public void Skips_message_with_existing_reasoning_content()
        {
            JsonObject body = Parse(@"{
                ""messages"": [
                    { ""role"": ""assistant"", ""content"": ""Hi."", ""reasoning_content"": ""Original thought."" }
                ]
            }");
            var history = new List<ReasoningEchoSnapshot>
            {
                new ReasoningEchoSnapshot("Hi.", new string[0], "Different thought."),
            };

            int injected = ReasoningEchoMatcher.InjectReasoning(body, history);

            Assert.Equal(0, injected);
            Assert.Equal("Original thought.", (body["messages"] as JsonArray)[0]["reasoning_content"]?.GetValue<string>());
        }

        [Fact]
        public void Skips_when_no_match_or_reasoning_empty()
        {
            JsonObject body = Parse(@"{
                ""messages"": [
                    { ""role"": ""assistant"", ""content"": ""Unknown text."" },
                    { ""role"": ""assistant"", ""content"": ""Known text."" }
                ]
            }");
            var history = new List<ReasoningEchoSnapshot>
            {
                new ReasoningEchoSnapshot("Known text.", new string[0], ""),
            };
            string before = body.ToJsonString();

            int injected = ReasoningEchoMatcher.InjectReasoning(body, history);

            Assert.Equal(0, injected);
            Assert.Equal(before, body.ToJsonString());
        }

        [Fact]
        public void Handles_parts_array_content()
        {
            JsonObject body = Parse(@"{
                ""messages"": [
                    { ""role"": ""assistant"", ""content"": [{ ""type"": ""text"", ""text"": ""Part one."" }, { ""type"": ""text"", ""text"": "" Part two."" }] }
                ]
            }");
            var history = new List<ReasoningEchoSnapshot>
            {
                new ReasoningEchoSnapshot("Part one. Part two.", new string[0], "Reasoned."),
            };

            int injected = ReasoningEchoMatcher.InjectReasoning(body, history);

            Assert.Equal(1, injected);
            Assert.Equal("Reasoned.", (body["messages"] as JsonArray)[0]["reasoning_content"]?.GetValue<string>());
        }

        [Fact]
        public void Ignores_non_assistant_roles_and_missing_messages()
        {
            JsonObject withoutMessages = Parse(@"{ ""model"": ""x"", ""input"": [] }");
            Assert.Equal(0, ReasoningEchoMatcher.InjectReasoning(withoutMessages, new List<ReasoningEchoSnapshot>
            {
                new ReasoningEchoSnapshot("Hi.", new string[0], "Thought."),
            }));
            Assert.Equal(0, ReasoningEchoMatcher.InjectReasoning(null, new List<ReasoningEchoSnapshot>()));
            Assert.Equal(0, ReasoningEchoMatcher.InjectReasoning(Parse(@"{""messages"":[]}"), null));
        }

        private static JsonObject Parse(string json)
        {
            return JsonNode.Parse(json) as JsonObject;
        }
    }
}
