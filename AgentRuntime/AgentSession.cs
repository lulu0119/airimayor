using System;

namespace AgentRuntime
{
    /// <summary>
    /// The chat's session: prompt, cancel, and the update stream.
    /// The built-in runtime and an external ACP head both sit here.
    /// </summary>
    public interface IAgentSession : IDisposable
    {
        event Action<SessionUpdate> Updated;

        AgentObservability Timeline { get; }

        void Prompt(string text);

        void Cancel();

        string ChatStateJson();
    }
}
