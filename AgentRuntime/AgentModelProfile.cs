using System;

namespace AgentRuntime
{
    /// <summary>
    /// Window and vision taken from the player settings. The model name is never parsed.
    /// </summary>
    internal sealed class AgentModelProfile
    {
        private readonly ModelCapabilities m_Caps;

        private AgentModelProfile(ModelCapabilities caps, bool visionAvailable)
        {
            m_Caps = caps;
            VisionAvailable = visionAvailable;
        }

        public long ContextWindowTokens => m_Caps.ContextWindowTokens;
        public long CompactAtTokens => m_Caps.CompactAtTokens;
        public long OutputReserveTokens => m_Caps.OutputReserveTokens;
        public long TailBudgetTokens => m_Caps.TailBudgetTokens;
        public bool VisionAvailable { get; }
        public string Source => m_Caps.Source;

        public static AgentModelProfile Resolve(
            long windowTokens,
            bool visionOn,
            string apiKindName)
        {
            long context = Math.Max(16_000, windowTokens > 0 ? windowTokens : 200_000);
            var caps = new ModelCapabilities
            {
                ContextWindowTokens = context,
                SupportsVision = visionOn,
                Source = string.IsNullOrEmpty(apiKindName) ? "player" : apiKindName,
            };
            return new AgentModelProfile(caps, visionOn);
        }
    }
}
