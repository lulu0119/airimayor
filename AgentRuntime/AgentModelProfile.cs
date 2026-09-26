using System;

namespace AgentRuntime
{
    /// <summary>
    /// Window and vision taken from the player settings. The model name is never parsed.
    /// </summary>
    internal sealed class AgentModelProfile
    {
        private AgentModelProfile(long contextWindowTokens, bool visionAvailable, string source)
        {
            ContextWindowTokens = contextWindowTokens;
            VisionAvailable = visionAvailable;
            Source = source;
        }

        public long ContextWindowTokens { get; }
        public bool VisionAvailable { get; }
        public string Source { get; }

        /// <summary>Tokens reserved so one reply fits in the window.</summary>
        public long OutputReserveTokens => Math.Min(32_000, Math.Max(1, ContextWindowTokens - 8_000));

        /// <summary>Estimated input size at which compaction runs.</summary>
        public long CompactAtTokens => ContextWindowTokens - OutputReserveTokens;

        /// <summary>Recent tokens kept verbatim during compaction.</summary>
        public long TailBudgetTokens => Math.Min(15_000, Math.Max(2_000, CompactAtTokens / 4));

        public static AgentModelProfile Resolve(
            long windowTokens,
            bool visionOn,
            string apiKindName)
        {
            long context = Math.Max(16_000, windowTokens > 0 ? windowTokens : 200_000);
            string source = string.IsNullOrEmpty(apiKindName) ? "player" : apiKindName;
            return new AgentModelProfile(context, visionOn, source);
        }
    }
}
