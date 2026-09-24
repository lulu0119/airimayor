using System.Text.Json;
using AgentRuntime;
using Xunit;

namespace CitiesSkylines2Agent.Agent
{
    public sealed class SessionPlanTests
    {
        private const string TwoSteps =
            "{\"entries\":[" +
            "{\"content\":\"fill residential demand\",\"priority\":\"high\",\"status\":\"in_progress\"}," +
            "{\"content\":\"clear homelessness\",\"priority\":\"medium\",\"status\":\"pending\"}" +
            "]}";

        [Fact]
        public void Empty_plan_emits_nothing()
        {
            var plan = new SessionPlan();
            Assert.Equal("", plan.ToUiJson());
        }

        [Fact]
        public void SetPlan_replaces_the_list_and_returns_those_steps()
        {
            var plan = new SessionPlan();
            PlanCallResult result = plan.SetPlan(TwoSteps);
            Assert.True(result.Success);
            Assert.Equal(plan.ToUiJson(), result.Text);
            using (JsonDocument ui = JsonDocument.Parse(result.Text))
            {
                JsonElement entries = ui.RootElement.GetProperty("entries");
                Assert.Equal(2, entries.GetArrayLength());
                Assert.Equal("fill residential demand", entries[0].GetProperty("content").GetString());
                Assert.Equal("high", entries[0].GetProperty("priority").GetString());
                Assert.Equal("in_progress", entries[0].GetProperty("status").GetString());
                Assert.Equal("clear homelessness", entries[1].GetProperty("content").GetString());
                Assert.Equal("medium", entries[1].GetProperty("priority").GetString());
                Assert.Equal("pending", entries[1].GetProperty("status").GetString());
            }
        }

        [Fact]
        public void A_later_set_plan_replaces_the_whole_list()
        {
            var plan = new SessionPlan();
            plan.SetPlan(TwoSteps);
            PlanCallResult result = plan.SetPlan(
                "{\"entries\":[{\"content\":\"sewage\",\"priority\":\"low\",\"status\":\"completed\"}]}");
            Assert.True(result.Success);
            using (JsonDocument ui = JsonDocument.Parse(plan.ToUiJson()))
            {
                JsonElement entries = ui.RootElement.GetProperty("entries");
                Assert.Equal(1, entries.GetArrayLength());
                Assert.Equal("sewage", entries[0].GetProperty("content").GetString());
            }
        }

        [Fact]
        public void Clear_returns_to_no_plan()
        {
            var plan = new SessionPlan();
            plan.SetPlan(TwoSteps);
            plan.Clear();
            Assert.Equal("", plan.ToUiJson());
        }

        [Fact]
        public void Rejects_an_invalid_call_and_keeps_the_previous_plan()
        {
            var plan = new SessionPlan();
            plan.SetPlan(TwoSteps);
            string kept = plan.ToUiJson();

            PlanCallResult missing = plan.SetPlan("{\"success\":\"demand 0\"}");
            Assert.False(missing.Success);
            Assert.Contains("entries is required", missing.Text);

            PlanCallResult empty = plan.SetPlan("{\"entries\":[]}");
            Assert.False(empty.Success);
            Assert.Contains("entries must contain one step", empty.Text);

            PlanCallResult blank = plan.SetPlan(
                "{\"entries\":[{\"content\":\"  \",\"priority\":\"high\",\"status\":\"pending\"}]}");
            Assert.False(blank.Success);
            Assert.Contains("content is required", blank.Text);

            PlanCallResult priority = plan.SetPlan(
                "{\"entries\":[{\"content\":\"sewage\",\"priority\":\"urgent\",\"status\":\"pending\"}]}");
            Assert.False(priority.Success);
            Assert.Contains("priority must be high, medium, or low", priority.Text);

            PlanCallResult status = plan.SetPlan(
                "{\"entries\":[{\"content\":\"sewage\",\"priority\":\"high\",\"status\":\"cancelled\"}]}");
            Assert.False(status.Success);
            Assert.Contains("status must be pending, in_progress, or completed", status.Text);

            Assert.Equal(kept, plan.ToUiJson());
        }
    }
}
