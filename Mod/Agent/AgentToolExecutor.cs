using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace CitiesSkylines2Agent.Agent
{
    internal sealed class AgentToolExecutor
    {
        private readonly AgentToolSurface m_ToolSurface;
        private readonly AgentClientFactory m_ClientFactory;
        private readonly AgentObservability m_Observability;
        private readonly Action<AgentUiEvent> m_Emit;
        private readonly Action<ChatMessage> m_AppendHistory;

        public AgentToolExecutor(AgentToolSurface toolSurface, AgentClientFactory clientFactory,
            AgentObservability observability, Action<AgentUiEvent> emit, Action<ChatMessage> appendHistory)
        {
            m_ToolSurface = toolSurface;
            m_ClientFactory = clientFactory;
            m_Observability = observability;
            m_Emit = emit;
            m_AppendHistory = appendHistory;
        }

        public int FunctionCount { get; private set; }

        public void Reset()
        {
            FunctionCount = 0;
        }

        public async Task ExecuteAsync(IReadOnlyList<FunctionCallContent> toolCalls, CancellationToken cancellationToken)
        {
            m_Emit(new AgentUiEvent { Kind = "status", Status = AgentStatus.Working });
            foreach (FunctionCallContent call in toolCalls)
            {
                string argumentsJson = SerializeArguments(call.Arguments);
                Stopwatch timer = Stopwatch.StartNew();
                m_Emit(new AgentUiEvent { Kind = "tool", Tool = call.Name ?? call.CallId, Text = argumentsJson });
                ToolInvocationResult result;
                try
                {
                    result = await InvokeAsync(call.Name, argumentsJson, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    timer.Stop();
                    FunctionCount++;
                    m_Observability.Function(call.Name, argumentsJson, "tool call interrupted", false, timer.ElapsedMilliseconds, 0, "interrupted");
                    m_AppendHistory(new ChatMessage(ChatRole.Tool,
                        new List<AIContent> { new FunctionResultContent(call.CallId, "tool call interrupted") }));
                    throw;
                }
                timer.Stop();
                FunctionCount++;
                m_Observability.Function(call.Name, argumentsJson, result.Text, result.Success, timer.ElapsedMilliseconds, 0,
                    result.Success ? null : result.Text);
                // Completion pairs with the start event above by arrival order.
                // Status carries only the outcome color for the tool row.
                m_Emit(new AgentUiEvent
                {
                    Kind = "tool",
                    Tool = call.Name ?? call.CallId,
                    Text = TruncateToolText(result.Text),
                    Status = result.Success ? AgentStatus.Idle : AgentStatus.Error,
                });
                m_AppendHistory(new ChatMessage(ChatRole.Tool,
                    new List<AIContent> { new FunctionResultContent(call.CallId, result.Text) }));
                AppendToolImage(result.ImagePath);
            }
        }

        internal static string SerializeArguments(IDictionary<string, object> arguments)
        {
            return arguments == null || arguments.Count == 0 ? "{}" : JsonSerializer.Serialize(arguments);
        }

        private static string TruncateToolText(string text)
        {
            const int MaxToolTextLength = 800;
            if (string.IsNullOrEmpty(text) || text.Length <= MaxToolTextLength)
            {
                return text ?? "";
            }
            return text.Substring(0, MaxToolTextLength) + "…";
        }

        private async Task<ToolInvocationResult> InvokeAsync(string name, string argumentsJson, CancellationToken cancellationToken)
        {
            try
            {
                if (!m_ToolSurface.IsAvailable(name, m_ClientFactory.GetProfile()))
                {
                    return Error("tool is not available for this model or current settings");
                }
                ToolDefinition tool = ToolCatalog.Find(name);
                if (tool == null) return Error("unknown tool: " + name);

                return await AgentToolBridge.InvokeAsync(tool, argumentsJson, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                m_Observability.Error("tool", e.ToString());
                return Error(AgentObservability.RedactSecrets(e.Message));
            }
        }

        private void AppendToolImage(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !m_ClientFactory.GetProfile().VisionAvailable ||
                !File.Exists(imagePath)) return;
            try
            {
                byte[] image = File.ReadAllBytes(imagePath);
                if (image.Length == 0 || image.Length > 8 * 1024 * 1024)
                {
                    m_Observability.Error("vision-attach", "screenshot exceeds image attachment limit");
                    return;
                }
                m_AppendHistory(new ChatMessage(ChatRole.User, new List<AIContent>
                {
                    new TextContent("Screenshot returned by the screenshot tool."),
                    new DataContent(new ReadOnlyMemory<byte>(image), "image/png"),
                }));
            }
            catch (Exception e)
            {
                m_Observability.Error("vision-attach", e.ToString());
            }
        }

        private static ToolInvocationResult Error(string message)
        {
            return new ToolInvocationResult { Success = false, Text = JsonSerializer.Serialize(new { error = message }) };
        }
    }
}
