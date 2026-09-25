using Colossal.Logging;

namespace CS2MCP
{
    /// <summary>
    /// Minimal stand-in for the upstream mod entry point: the bridge code was
    /// inlined into airimayor, so it references this static info
    /// instead of the original CS2MCP.Mod entry point.
    /// </summary>
    public static class Mod
    {
        public const string Name = "airimayor (inlined CS2MCP bridge)";
        public const string Version = "0.8.2";

        public static readonly ILog Log =
            LogManager.GetLogger(nameof(CS2MCP)).SetShowsErrorsInUI(false);
    }
}
