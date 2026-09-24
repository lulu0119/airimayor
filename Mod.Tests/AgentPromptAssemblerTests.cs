using System.Collections.Generic;
using AgentRuntime;
using Microsoft.Extensions.AI;
using Xunit;

namespace CitiesSkylines2Agent.Agent
{
    public sealed class AgentPromptAssemblerTests
    {
        private const string Prompt = "mayor instructions";
        private const string SummaryPrefix = "[context summary] ";

        [Fact]
        public void Apply_pins_the_system_prompt()
        {
            var assembler = new AgentPromptAssembler(Prompt, SummaryPrefix);
            var history = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "grow"),
            };

            assembler.Apply(history);
            Assert.Equal(2, history.Count);
            Assert.Equal(Prompt, history[0].Text);
            Assert.Equal("grow", history[1].Text);

            assembler.Apply(history);
            Assert.Equal(2, history.Count);
        }

        [Fact]
        public void Apply_leaves_the_summary_and_kept_messages()
        {
            var assembler = new AgentPromptAssembler(Prompt, SummaryPrefix);
            var history = new List<ChatMessage>();
            assembler.Rebuild(history, "{\"session_state\":\"mayor loop\"}", new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "keep going"),
            });
            assembler.Apply(history);

            Assert.Equal(3, history.Count);
            Assert.Equal(Prompt, history[0].Text);
            Assert.Equal(SummaryPrefix + "{\"session_state\":\"mayor loop\"}", history[1].Text);
            Assert.Equal("keep going", history[2].Text);
        }
    }
}
