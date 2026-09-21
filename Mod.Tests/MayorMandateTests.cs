using System.Text.Json;
using Xunit;

namespace CitiesSkylines2Agent.Agent
{
    public sealed class MayorMandateTests
    {
        [Fact]
        public void Empty_live_note_asks_for_set_plan_and_does_not_wait_or_review_the_city()
        {
            var mandate = new MayorMandate();
            string note = mandate.LiveNote();
            Assert.StartsWith(MayorMandate.HistoryNotePrefix, note);
            Assert.Contains("set_plan", note);
            Assert.DoesNotContain("wait_simulation", note);
            Assert.DoesNotContain("review the whole city", note);
            Assert.Equal("", mandate.ToUiJson());
        }

        [Fact]
        public void SetPlan_then_live_note_restates_goal()
        {
            var mandate = new MayorMandate();
            MandateCallResult result = mandate.SetPlan(
                "{\"goal\":\"fill residential demand\",\"success\":\"homelessness gone\"}");
            Assert.True(result.Success);
            using (JsonDocument ui = JsonDocument.Parse(mandate.ToUiJson()))
            {
                Assert.Equal("fill residential demand", ui.RootElement.GetProperty("goal").GetString());
                Assert.Equal("homelessness gone", ui.RootElement.GetProperty("success").GetString());
                Assert.False(ui.RootElement.TryGetProperty("kind", out _));
            }

            string note = mandate.LiveNote();
            Assert.Contains("fill residential demand", note);
            Assert.Contains("homelessness gone", note);
            Assert.DoesNotContain("review the whole city", note);
            Assert.DoesNotContain("wait_simulation", note);
        }

        [Fact]
        public void Player_clear_returns_to_empty_note()
        {
            var mandate = new MayorMandate();
            mandate.SetPlan(
                "{\"goal\":\"sewage\",\"success\":\"no sewage notification\"}");
            mandate.Clear();
            Assert.Equal("", mandate.ToUiJson());
            Assert.Contains("none.", mandate.LiveNote());
        }

        [Fact]
        public void Rejects_missing_goal()
        {
            var mandate = new MayorMandate();
            MandateCallResult result = mandate.SetPlan(
                "{\"success\":\"demand 0\"}");
            Assert.False(result.Success);
            Assert.Equal("", mandate.ToUiJson());
            Assert.Contains("goal is required", result.Text);
        }
    }
}
