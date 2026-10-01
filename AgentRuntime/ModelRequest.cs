using System;
using System.Collections.Generic;
using Microsoft.Extensions.AI;

namespace AgentRuntime
{
    /// <summary>
    /// Options for one model call. Console Go rejects any tool choice other than auto.
    /// </summary>
    internal static class ModelRequest
    {
        internal static ChatOptions Options(string model, AgentModelProfile profile, IList<AITool> tools)
        {
            return new ChatOptions
            {
                ModelId = model,
                MaxOutputTokens = (int)Math.Min(int.MaxValue, profile.OutputReserveTokens),
                Tools = tools,
                ToolMode = ChatToolMode.Auto,
            };
        }
    }
}
