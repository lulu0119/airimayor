using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using AgentRuntime;
using Microsoft.Extensions.AI;
using Xunit;

namespace CitiesSkylines2Agent.Agent
{
    /// <summary>
    /// The reasoning_content echo (ReasoningEchoPolicy + CaptureReasoningSnapshots)
    /// only works if the reasoning that arrives in streaming updates survives the
    /// <c>updates.ToChatResponse().Messages</c> round-trip into history. This is the
    /// exact shape of the live 400: a text answer carrying reasoning, followed by a
    /// next-turn request that must carry reasoning_content back.
    /// </summary>
    public sealed class ReasoningRoundTripTests
    {
        [Fact]
        public void Streaming_text_reasoning_survives_ToChatResponse_into_history()
        {
            var updates = new List<ChatResponseUpdate>
            {
                new ChatResponseUpdate(ChatRole.Assistant, "City summary: 34k pop.") { ResponseId = "resp_1" },
            };
            updates[0].Contents.Add(new TextReasoningContent("Unemployment down to 14.8%."));

            ChatResponse response = updates.ToChatResponse();

            ChatMessage message = Assert.Single(response.Messages);
            Assert.Contains(message.Contents, content =>
                content is TextReasoningContent reasoning &&
                reasoning.Text == "Unemployment down to 14.8%.");
        }

        [Fact]
        public void Streaming_tool_call_reasoning_survives_ToChatResponse_into_history()
        {
            var updates = new List<ChatResponseUpdate>
            {
                new ChatResponseUpdate(ChatRole.Assistant, (string)null) { ResponseId = "resp_2" },
            };
            updates[0].Contents.Add(new FunctionCallContent("call_1", "labor", new Dictionary<string, object>()));
            updates[0].Contents.Add(new TextReasoningContent("Check labor then budget."));

            ChatResponse response = updates.ToChatResponse();

            ChatMessage message = Assert.Single(response.Messages);
            Assert.Contains(message.Contents, content =>
                content is FunctionCallContent call && call.CallId == "call_1");
            Assert.Contains(message.Contents, content =>
                content is TextReasoningContent reasoning &&
                reasoning.Text == "Check labor then budget.");
        }
        [Fact]
        public void Pipeline_request_content_reads_as_json_for_echo_matcher()
        {
            // ReasoningEchoPolicy must read the payload bytes: BinaryContent.ToString()
            // returns the type name, not the JSON, which silently disabled the echo.
            const string json = @"{""model"":""deepseek-v4.1-flash"",""messages"":[{""role"":""assistant"",""content"":""City summary: 34k pop.""}]}";
            BinaryContent content = BinaryContent.Create(BinaryData.FromString(json));

            JsonObject body = ReasoningEchoPolicy.TryReadBody(content);

            Assert.NotNull(body);
            int injected = ReasoningEchoMatcher.InjectReasoning(body, new List<ReasoningEchoSnapshot>
            {
                new ReasoningEchoSnapshot("City summary: 34k pop.", new string[0], "Unemployment down to 14.8%."),
            });
            Assert.Equal(1, injected);
        }

        [Fact]
        public void Echo_body_reader_returns_null_for_missing_content()
        {
            Assert.Null(ReasoningEchoPolicy.TryReadBody(null));
        }
    }
}
