using System;

namespace CitiesSkylines2Agent.Agent
{
    /// <summary>
    /// Resolved model capabilities from the player settings only. The loop
    /// never parses the model name: the request shape comes from ApiKind and
    /// the token window comes from WindowTokens.
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
                MaxOutputTokens = Math.Min(16_384, Math.Max(4_096, context / 10)),
                SupportsVision = visionOn,
                Source = string.IsNullOrEmpty(apiKindName) ? "player" : apiKindName,
            };
            return new AgentModelProfile(caps, visionOn);
        }
    }
}
