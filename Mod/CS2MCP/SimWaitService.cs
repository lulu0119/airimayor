using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CS2MCP
{
    /// <summary>
    /// City-side follow-up for a timed simulation wait: blocks the agent
    /// thread until the run finishes, then returns the city snapshot digest
    /// as the wait tool result. The generic agent loop never sees which
    /// snapshot routes or fields make up that digest.
    /// </summary>
    internal static class SimWaitService
    {
        private const int SimWaitPollMs = 250;

        public static async Task<string> WaitAndDigestAsync(
            BridgeSystem bridge,
            string startJson,
            int requestedHours,
            CancellationToken cancellationToken)
        {
            // At the game's high speed 8x, one game hour takes roughly
            // 20-30 real seconds. Allow up to 5 minutes per game hour so slow
            // hardware never makes the agent think a wait is stuck.
            int maxWaitMs = requestedHours * 300_000 + SimWaitPollMs * 4;
            int waited = 0;
            while (bridge.AutoPauseTargetFrame != 0 && waited < maxWaitMs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(SimWaitPollMs, cancellationToken);
                waited += SimWaitPollMs;
            }

            Task<string> overview = TryGetJsonAsync(bridge, "/city/overview");
            Task<string> state = TryGetJsonAsync(bridge, "/state");
            Task<string> notifications = TryGetJsonAsync(bridge, "/city/notifications");
            Task<string> services = TryGetJsonAsync(bridge, "/city/services");
            await Task.WhenAll(overview, state, notifications, services);
            return SimWaitDigest.Build(
                startJson,
                await overview,
                await notifications,
                await services,
                await state,
                bridge.AutoPauseTargetFrame == 0);
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
