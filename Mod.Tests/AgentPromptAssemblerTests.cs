using System.Collections.Generic;
using Microsoft.Extensions.AI;
using Xunit;

namespace CitiesSkylines2Agent.Agent
{
    public sealed class AgentPromptAssemblerTests
    {
        private const string Prompt = "mayor instructions";
        private const string SummaryPrefix = "[context summary] ";

        [Fact]
        public void Apply_keeps_one_live_note_and_replaces_it()
        {
            var assembler = new AgentPromptAssembler(Prompt, SummaryPrefix);
            var history = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "grow"),
            };

            assembler.Apply(history, MayorMandate.HistoryNotePrefix + "goal=a");
            Assert.Equal(3, history.Count);
            Assert.Equal(Prompt, history[0].Text);
            Assert.StartsWith(MayorMandate.HistoryNotePrefix, history[1].Text);
            Assert.Equal("grow", history[2].Text);

            assembler.Apply(history, MayorMandate.HistoryNotePrefix + "goal=b");
            Assert.Equal(3, history.Count);
            Assert.Equal(MayorMandate.HistoryNotePrefix + "goal=b", history[1].Text);
        }

        [Fact]
        public void Apply_pins_live_note_after_summary()
        {
            var assembler = new AgentPromptAssembler(Prompt, SummaryPrefix);
            var history = new List<ChatMessage>();
            assembler.Rebuild(history, "{\"session_state\":\"mayor loop\"}", new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "keep going"),
                new ChatMessage(ChatRole.System, MayorMandate.HistoryNotePrefix + "stale"),
            });
            assembler.Apply(history, MayorMandate.HistoryNotePrefix + "goal=fresh");

            Assert.Equal(Prompt, history[0].Text);
            Assert.Equal(SummaryPrefix + "{\"session_state\":\"mayor loop\"}", history[1].Text);
            Assert.Equal(MayorMandate.HistoryNotePrefix + "goal=fresh", history[2].Text);
            Assert.Equal("keep going", history[3].Text);
            Assert.Equal(4, history.Count);
        }
    }
}
