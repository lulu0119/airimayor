using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgentRuntime
{
    /// <summary>
    /// Game tools the runtime can list and call. set_plan is not in this list.
    /// </summary>
    public interface IAgentTools
    {
        IReadOnlyList<AgentToolDeclaration> List(bool visionAvailable);

        Task<AgentToolResult> InvokeAsync(
            string name,
            string argumentsJson,
            CancellationToken cancellationToken);
    }

    public sealed class AgentToolDeclaration
    {
        public string Name;
        public string Description;
        public string ParametersJson;
    }

    public sealed class AgentToolResult
    {
        public bool Success;
        public string Text;
        public byte[] ImagePng;
        public string ImageCaption;
        public byte[] PreviewJpeg;
    }

    public enum ModelWire
    {
        ChatCompletions,
        Responses,
    }

    public sealed class ModelSettings
    {
        public string Endpoint;
        public string ApiKey;
        public string Model;
        public long WindowTokens;
        public bool Vision;
        public bool ContinueWhenIdle;
        public ModelWire Wire;
    }
}
