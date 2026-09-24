using System.Text.Json;
using AgentRuntime;
using Xunit;

namespace CitiesSkylines2Agent.Agent
{
    public sealed class SessionPlanTests
    {
        [Fact]
        public void Empty_live_note_asks_for_set_plan_and_does_not_wait_or_review_the_city()
        {
            var plan = new SessionPlan();
            string note = plan.LiveNote();
            Assert.StartsWith(SessionPlan.HistoryNotePrefix, note);
            Assert.Contains("set_plan", note);
            Assert.DoesNotContain("wait_simulation", note);
            Assert.DoesNotContain("review the whole city", note);
            Assert.Equal("", plan.ToUiJson());
        }

        [Fact]
        public void SetPlan_then_live_note_restates_goal()
        {
            var plan = new SessionPlan();
            PlanCallResult result = plan.SetPlan(
                "{\"goal\":\"fill residential demand\",\"success\":\"homelessness gone\"}");
            Assert.True(result.Success);
            using (JsonDocument ui = JsonDocument.Parse(plan.ToUiJson()))
            {
                Assert.Equal("fill residential demand", ui.RootElement.GetProperty("goal").GetString());
                Assert.Equal("homelessness gone", ui.RootElement.GetProperty("success").GetString());
                Assert.False(ui.RootElement.TryGetProperty("kind", out _));
            }

            string note = plan.LiveNote();
            Assert.Contains("fill residential demand", note);
            Assert.Contains("homelessness gone", note);
            Assert.DoesNotContain("player message", note);
            Assert.DoesNotContain("review the whole city", note);
            Assert.DoesNotContain("wait_simulation", note);
        }

        [Fact]
        public void Clear_returns_to_empty_note()
        {
            var plan = new SessionPlan();
            plan.SetPlan(
                "{\"goal\":\"sewage\",\"success\":\"no sewage notification\"}");
            plan.Clear();
            Assert.Equal("", plan.ToUiJson());
            Assert.Contains("none.", plan.LiveNote());
        }

        [Fact]
        public void Rejects_missing_goal()
        {
            var plan = new SessionPlan();
            PlanCallResult result = plan.SetPlan(
                "{\"success\":\"demand 0\"}");
            Assert.False(result.Success);
            Assert.Equal("", plan.ToUiJson());
            Assert.Contains("goal is required", result.Text);
        }
    }
}
