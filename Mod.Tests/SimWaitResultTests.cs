using System.Text.Json;
using CS2MCP;
using Xunit;

namespace CS2MCP
{
    public sealed class SimWaitResultTests
    {
        private const string WaitJson =
            "{\"running\":true,\"hours\":4,\"speed\":8,\"restoreSpeed\":0,\"startFrame\":10,\"targetFrame\":100," +
            "\"note\":\"simulation runs until exactly the requested in-game hours have passed\"}";
        private const string StateReached = "{\"simulation\":{\"frameIndex\":100}}";
        private const string StateShort = "{\"simulation\":{\"frameIndex\":99}}";

        [Fact]
        public void Returns_wait_mechanics_without_city_snapshot()
        {
            using (JsonDocument document = Parse(true, StateReached))
            {
                JsonElement root = document.RootElement;
                Assert.Equal(4, root.GetProperty("hours").GetInt32());
                Assert.True(root.GetProperty("completed").GetBoolean());
                Assert.True(root.GetProperty("targetReached").GetBoolean());
                Assert.Equal(
                    "wait finished; simulation restored to its previous speed/pause state",
                    root.GetProperty("note").GetString());
                Assert.False(root.TryGetProperty("overview", out _));
                Assert.False(root.TryGetProperty("problems", out _));
                Assert.False(root.TryGetProperty("population", out _));
                Assert.False(root.TryGetProperty("cityName", out _));
            }
        }

        [Fact]
        public void Strips_sim_wait_internals_and_defaults_hours()
        {
            using (JsonDocument document = Parse(true, StateReached))
            {
                JsonElement root = document.RootElement;
                Assert.False(root.TryGetProperty("running", out _));
                Assert.False(root.TryGetProperty("speed", out _));
                Assert.False(root.TryGetProperty("restoreSpeed", out _));
                Assert.False(root.TryGetProperty("startFrame", out _));
                Assert.False(root.TryGetProperty("targetFrame", out _));
                Assert.False(root.TryGetProperty("waitedMs", out _));
            }

            using (JsonDocument document = JsonDocument.Parse(SimWaitResult.Build("{", "[]", true)))
            {
                JsonElement root = document.RootElement;
                Assert.Equal(1, root.GetProperty("hours").GetInt32());
                Assert.True(root.GetProperty("completed").GetBoolean());
                Assert.False(root.GetProperty("targetReached").GetBoolean());
            }
        }

        [Theory]
        [InlineData(false, true, "wait did not finish in time; retry wait_simulation once")]
        [InlineData(true, false, "wait aborted: simulation did not advance (game paused or a modal overlay is open)")]
        [InlineData(true, true, "wait finished; simulation restored to its previous speed/pause state")]
        public void Uses_the_three_stable_notes(bool completed, bool reached, string note)
        {
            using (JsonDocument document = Parse(completed, reached ? StateReached : StateShort))
            {
                Assert.Equal(note, document.RootElement.GetProperty("note").GetString());
                Assert.Equal(completed, document.RootElement.GetProperty("completed").GetBoolean());
                Assert.Equal(reached, document.RootElement.GetProperty("targetReached").GetBoolean());
            }
        }

        private static JsonDocument Parse(bool completed, string stateJson)
        {
            return JsonDocument.Parse(
                SimWaitResult.Build(
                    WaitJson,
                    stateJson,
                    completed));
        }
    }
}
