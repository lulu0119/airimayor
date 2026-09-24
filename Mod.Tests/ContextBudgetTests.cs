using AgentRuntime;
using Xunit;

namespace CitiesSkylines2Agent.Agent
{
    public sealed class ContextBudgetTests
    {
        [Theory]
        [InlineData(16_000, 8_000, 8_000, 2_000)]
        [InlineData(200_000, 32_000, 168_000, 15_000)]
        [InlineData(2_000_000, 32_000, 1_968_000, 15_000)]
        public void Window_derives_output_compact_and_tail(
            long window,
            long output,
            long compact,
            long tail)
        {
            var caps = new ModelCapabilities { ContextWindowTokens = window };
            Assert.Equal(output, caps.OutputReserveTokens);
            Assert.Equal(compact, caps.CompactAtTokens);
            Assert.Equal(tail, caps.TailBudgetTokens);
            Assert.Equal(window, caps.CompactAtTokens + caps.OutputReserveTokens);
        }

        [Fact]
        public void Aggressive_tail_is_half_and_never_larger_than_the_normal_tail()
        {
            Assert.Equal(2_000, AgentContextBudget.TailBudget(2_000, false));
            Assert.Equal(1_000, AgentContextBudget.TailBudget(2_000, true));
            Assert.Equal(15_000, AgentContextBudget.TailBudget(15_000, false));
            Assert.Equal(7_500, AgentContextBudget.TailBudget(15_000, true));
        }
    }
}
