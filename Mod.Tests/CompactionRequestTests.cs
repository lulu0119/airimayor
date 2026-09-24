using System.Collections.Generic;
using AgentRuntime;
using CitiesSkylines2Agent.Agent;
using Microsoft.Extensions.AI;
using Xunit;

namespace CitiesSkylines2Agent.Agent
{
    /// <summary>
    /// Pins the working Responses compaction shape: normal instructions and
    /// history go in intact, the summary task is only appended. Guards the
    /// Chat Completions fix against regressing this path.
    /// </summary>
    public sealed class CompactionRequestTests
    {
        private const string TaskPrompt = "COMPACTION TASK: return strict JSON.";

        private const string RealSummary =
            "{\"time_anchor\":\"frame ~7487488\",\"session_state\":\"mayor loop\"," +
            "\"active_commitments\":[],\"durable_facts\":[]," +
            "\"relevant_people\":[],\"open_loops\":[],\"recent_timeline\":[]," +
            "\"forgettable_noise\":[],\"paused_state\":\"\"," +
            "\"last_world_snapshot\":\"pop 3000\"}";

        [Fact]
        public void Keeps_system_and_history_roles_with_task_appended_last()
        {
            var old = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "mayor instructions"),
                new ChatMessage(ChatRole.User, "grow the city"),
                new ChatMessage(ChatRole.Assistant, "on it"),
            };

            List<ChatMessage> input = AgentContextBudget.BuildSummaryInput(old, TaskPrompt);

            Assert.Equal(old.Count + 1, input.Count);
            Assert.Same(old[0], input[0]);
            Assert.Same(old[1], input[1]);
            Assert.Same(old[2], input[2]);
            Assert.Equal(ChatRole.User, input[3].Role);
            Assert.Equal(TaskPrompt, input[3].Text);
        }

        [Fact]
        public void Accepts_real_responses_summary_shape()
        {
            Assert.True(AgentContextBudget.IsUsableSummary(RealSummary));
        }

        [Fact]
        public void Rejects_empty_and_tool_markup_summaries()
        {
            Assert.False(AgentContextBudget.IsUsableSummary(""));
            Assert.False(AgentContextBudget.IsUsableSummary("   "));
            Assert.False(AgentContextBudget.IsUsableSummary("{\"tool_calls\":[]}"));
        }
    }
}
