using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace CS2MCP
{
    /// <summary>
    /// City-side follow-up for a timed simulation wait: blocks the agent
    /// thread until the run finishes, then returns wait mechanics only
    /// (hours/completed/targetReached/note). No city snapshot is attached;
    /// the model reads overview and problems through the read tools.
    /// </summary>
    internal static class SimWaitService
    {
        private const int SimWaitPollMs = 250;

        public static async Task<string> WaitAsync(
            BridgeSystem bridge,
            string startJson,
            CancellationToken cancellationToken)
        {
            // The timed run was already started city-side from these same
            // hours, so derive the wall-clock budget from the echoed JSON:
            // one parse for both sides, no second reading of the query.
            double requestedHours = RequestedHours(startJson);
            // At the game's high speed 8x, one game hour takes roughly
            // 20-30 real seconds. Allow up to 5 minutes per game hour so slow
            // hardware never makes the agent think a wait is stuck.
            int maxWaitMs = (int)(Math.Ceiling(requestedHours) * 300_000) + SimWaitPollMs * 4;
            int waited = 0;
            try
            {
                while (bridge.AutoPauseTargetFrame != 0 && waited < maxWaitMs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(SimWaitPollMs, cancellationToken);
                    waited += SimWaitPollMs;
                }
            }
            catch (OperationCanceledException)
            {
                // The player already took the clock: do not restore over it.
                if (bridge.LastWaitOutcome == WaitOutcome.TakenOver)
                {
                    string takenOver = await TryGetJsonAsync(bridge, "/state");
                    return SimWaitResult.Build(
                        startJson,
                        takenOver,
                        false,
                        SimWaitResult.TakenOverNote);
                }
                // Stop ends the advance early: restore the clock so the
                // player never inherits the run speed.
                bridge.CancelTimedRun();
                string state = await TryGetJsonAsync(bridge, "/state");
                return SimWaitResult.Build(
                    startJson,
                    state,
                    false,
                    SimWaitResult.InterruptedNote);
            }

            string finalState = await TryGetJsonAsync(bridge, "/state");
            if (bridge.LastWaitOutcome == WaitOutcome.TakenOver)
            {
                return SimWaitResult.Build(
                    startJson,
                    finalState,
                    false,
                    SimWaitResult.TakenOverNote);
            }
            return SimWaitResult.Build(
                startJson,
                finalState,
                bridge.AutoPauseTargetFrame == 0);
        }

        private static double RequestedHours(string startJson)
        {
            if (string.IsNullOrWhiteSpace(startJson))
            {
                return 1;
            }
            try
            {
                JsonObject root = JsonNode.Parse(startJson) as JsonObject;
                if (root?["hours"] is JsonValue hours &&
                    hours.TryGetValue<double>(out double parsed) &&
                    parsed > 0)
                {
                    return parsed;
                }
            }
            catch (JsonException)
            {
            }
            return 1;
        }

        private static async Task<string> TryGetJsonAsync(BridgeSystem bridge, string route)
        {
            try
            {
                BridgeResponse response = await bridge.InvokeAsync(route);
                if (response != null && response.Success)
                {
                    return Encoding.UTF8.GetString(response.Body ?? Array.Empty<byte>());
                }
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"post-wait {route} failed: {e.Message}");
            }
            return null;
        }
    }
}
