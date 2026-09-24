using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace AgentRuntime
{
    /// <summary>
    /// Per-call pipeline policy that echoes stored thinking-model reasoning
    /// back as <c>reasoning_content</c> on Chat Completions requests. Runs
    /// after MEAI serialization, so it covers the reasoning MEAI drops. Only
    /// assistant messages with known reasoning change; everything else stays
    /// byte-identical. Never throws: any failure leaves the request untouched.
    /// </summary>
    internal sealed class ReasoningEchoPolicy : PipelinePolicy
    {
        private readonly Func<IReadOnlyList<ReasoningEchoSnapshot>> m_Snapshots;
        private readonly Action<int> m_OnInjected;

        public ReasoningEchoPolicy(
            Func<IReadOnlyList<ReasoningEchoSnapshot>> snapshots,
            Action<int> onInjected = null)
        {
            m_Snapshots = snapshots;
            m_OnInjected = onInjected;
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
            try
            {
                if (message?.Request?.Content == null)
                {
                    return;
                }
                if (!string.Equals(message.Request.Method, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                IReadOnlyList<ReasoningEchoSnapshot> snapshots = m_Snapshots?.Invoke();
                if (snapshots == null || snapshots.Count == 0)
                {
                    return;
                }
                if (!(TryReadBody(message.Request.Content) is JsonObject body))
                {
                    return;
                }
                int injected = ReasoningEchoMatcher.InjectReasoning(body, snapshots);
                if (injected > 0)
                {
                    message.Request.Content = BinaryContent.Create(BinaryData.FromString(body.ToJsonString()));
                    m_OnInjected?.Invoke(injected);
                }
            }
            catch
            {
                // The request path must never break because of echo injection.
            }
        }

        /// <summary>
        /// Reads the serialized request JSON. <c>BinaryContent.ToString()</c>
        /// returns the type name, not the payload, so the bytes must be copied
        /// out explicitly. Returns null when the body is missing or not JSON.
        /// </summary>
        internal static JsonObject TryReadBody(BinaryContent content)
        {
            if (content == null)
            {
                return null;
            }
            using (var stream = new MemoryStream())
            {
                content.WriteTo(stream);
                return JsonNode.Parse(Encoding.UTF8.GetString(stream.ToArray())) as JsonObject;
            }
        }
    }
}
