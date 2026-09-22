using System;

namespace CitiesSkylines2Agent.Agent
{
    internal sealed class ModelCapabilities
    {
        public long ContextWindowTokens { get; set; }
        public bool SupportsVision { get; set; }
        public string Source { get; set; } = "";

        /// <summary>Tokens reserved so one reply fits in the window.</summary>
        public long OutputReserveTokens => Math.Min(32_000, Math.Max(1, ContextWindowTokens - 8_000));

        /// <summary>Estimated input size at which compaction runs.</summary>
        public long CompactAtTokens => ContextWindowTokens - OutputReserveTokens;

        /// <summary>Recent tokens kept verbatim during compaction.</summary>
        public long TailBudgetTokens => Math.Min(15_000, Math.Max(2_000, CompactAtTokens / 4));
    }
}
