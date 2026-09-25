using System;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AgentRuntime
{
    /// <summary>
    /// Adds a stable per-session id and the agent's own user agent to
    /// every model request. OpenCode Go rejects requests without
    /// <c>x-opencode-session</c>; the header is set globally so the provider
    /// does not matter.
    /// </summary>
    internal sealed class ConversationHeaderPolicy : PipelinePolicy
    {
        private const string SessionHeader = "x-opencode-session";
        private const string UserAgent = "airimayor/1.0";

        private readonly string m_SessionId;

        public ConversationHeaderPolicy(string sessionId)
        {
            m_SessionId = string.IsNullOrEmpty(sessionId)
                ? Guid.NewGuid().ToString("N")
                : sessionId;
        }

        public override void Process(
            PipelineMessage message,
            IReadOnlyList<PipelinePolicy> pipeline,
            int currentIndex)
        {
            Apply(message);
            ProcessNext(message, pipeline, currentIndex);
        }

        public override ValueTask ProcessAsync(
            PipelineMessage message,
            IReadOnlyList<PipelinePolicy> pipeline,
            int currentIndex)
        {
            Apply(message);
            return ProcessNextAsync(message, pipeline, currentIndex);
        }

        private void Apply(PipelineMessage message)
        {
            message.Request.Headers.Set(SessionHeader, m_SessionId);
            message.Request.Headers.Set("User-Agent", UserAgent);
        }
    }
}
