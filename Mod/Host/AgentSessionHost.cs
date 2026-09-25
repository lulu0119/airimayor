using AgentRuntime;

namespace airimayor.Host
{
    /// <summary>
    /// The loaded-city runtime. The UI system creates and clears it.
    /// Mod unload disposes it if the UI system has not yet.
    /// </summary>
    internal static class AgentSessionHost
    {
        public static AgentRuntime.AgentRuntime Current { get; set; }
    }
}
