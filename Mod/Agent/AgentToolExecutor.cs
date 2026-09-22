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
        private readonly MayorMandate m_Mandate;
        private readonly Action<AgentUiEvent> m_Emit;
        private readonly Action<ChatMessage> m_AppendHistory;
        private readonly Action m_OnPlanChanged;
        private readonly Func<CancellationToken, CancellationToken> m_BeginAdvance;
        private readonly Action m_EndAdvance;

        public AgentToolExecutor(AgentToolSurface toolSurface, AgentClientFactory clientFactory,
            AgentObservability observability, MayorMandate mandate, Action<AgentUiEvent> emit,
            Action<ChatMessage> appendHistory, Action onPlanChanged,
            Func<CancellationToken, CancellationToken> beginAdvance, Action endAdvance)
        {
            m_ToolSurface = toolSurface;
            m_ClientFactory = clientFactory;
            m_Observability = observability;
            m_Mandate = mandate;
            m_Emit = emit;
            m_AppendHistory = appendHistory;
            m_OnPlanChanged = onPlanChanged;
            m_BeginAdvance = beginAdvance;
            m_EndAdvance = endAdvance;
        }

        public int FunctionCount { get; private set; }

        public void Reset()
        {
            FunctionCount = 0;
        }

        public async Task ExecuteAsync(IReadOnlyList<FunctionCallContent> toolCalls, CancellationToken cancellationToken)
        {
            m_Emit(new AgentUiEvent { Kind = "status", Status = AgentStatus.Working });
            // Image previews ride after every tool result of this generation:
            // interleaving them between tool messages breaks the Chat
            // Completions pairing (assistant tool_calls must be followed by
            // consecutive tool results).
            var pendingImages = new List<KeyValuePair<string, string>>();
            bool stopBatch = false;
            for (int index = 0; index < toolCalls.Count; index++)
            {
                FunctionCallContent call = toolCalls[index];
                string argumentsJson = SerializeArguments(call.Arguments);
                bool advance = IsTimeAdvance(call.Name, argumentsJson);
                CancellationToken callToken = advance ? m_BeginAdvance(cancellationToken) : cancellationToken;
                Stopwatch timer = Stopwatch.StartNew();
                m_Emit(new AgentUiEvent { Kind = "tool", Tool = call.Name ?? call.CallId, Text = argumentsJson });
                ToolInvocationResult result;
                try
                {
                    result = await InvokeAsync(call.Name, argumentsJson, callToken);
                }
                catch (OperationCanceledException)
                {
                    timer.Stop();
                    RecordInterrupted(call, argumentsJson, timer.ElapsedMilliseconds);
                    PoisonRemaining(toolCalls, index + 1);
                    throw;
                }
                finally
                {
                    if (advance)
                    {
                        m_EndAdvance();
                    }
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
                    Image = ToPreviewDataUri(result.PreviewBytes),
                    ImageWidth = result.PreviewWidth,
                    ImageHeight = result.PreviewHeight,
                });
                m_AppendHistory(new ChatMessage(ChatRole.Tool,
                    new List<AIContent> { new FunctionResultContent(call.CallId, result.Text) }));
                if (!string.IsNullOrWhiteSpace(result.ImagePath))
                {
                    pendingImages.Add(new KeyValuePair<string, string>(call.Name, result.ImagePath));
                }
                if (advance && callToken.IsCancellationRequested)
                {
                    PoisonRemaining(toolCalls, index + 1);
                    stopBatch = true;
                    break;
                }
            }
            foreach (KeyValuePair<string, string> pending in pendingImages)
            {
                AppendToolImage(pending.Key, pending.Value);
            }
            if (stopBatch && cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        private static bool IsTimeAdvance(string name, string argumentsJson)
        {
            if (!string.Equals(name, "set_simulation", StringComparison.Ordinal))
            {
                return false;
            }
            using (JsonDocument document = JsonDocument.Parse(argumentsJson))
            {
                JsonElement root = document.RootElement;
                return root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("action", out JsonElement action) &&
                    action.ValueKind == JsonValueKind.String &&
                    string.Equals(action.GetString(), "advance", StringComparison.Ordinal);
            }
        }

        private void RecordInterrupted(FunctionCallContent call, string argumentsJson, long elapsedMilliseconds)
        {
            FunctionCount++;
            m_Observability.Function(call.Name, argumentsJson, "tool call interrupted", false, elapsedMilliseconds, 0, "interrupted");
            m_AppendHistory(new ChatMessage(ChatRole.Tool,
                new List<AIContent> { new FunctionResultContent(call.CallId, "tool call interrupted") }));
        }

        /// <summary>
        /// Every remaining call in this batch still gets a result, or the
        /// orphaned tool_calls break Chat Completions pairing. No UI events:
        /// their start rows were never emitted.
        /// </summary>
        private void PoisonRemaining(IReadOnlyList<FunctionCallContent> toolCalls, int start)
        {
            for (int rest = start; rest < toolCalls.Count; rest++)
            {
                FunctionCallContent skipped = toolCalls[rest];
                RecordInterrupted(skipped, SerializeArguments(skipped.Arguments), 0);
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

        /// <summary>
        /// UI-only preview carrier. Base64 over the event binding is pure .NET
        /// (safe on the agent thread); the thumbnail itself was rendered on the
        /// main thread at capture time. Oversized payloads stay text-only.
        /// </summary>
        private static string ToPreviewDataUri(byte[] preview)
        {
            const int MaxPreviewBytes = 256 * 1024;
            if (preview == null || preview.Length == 0 || preview.Length > MaxPreviewBytes)
            {
                return null;
            }
            return "data:image/jpeg;base64," + Convert.ToBase64String(preview);
        }

        private async Task<ToolInvocationResult> InvokeAsync(string name, string argumentsJson, CancellationToken cancellationToken)
        {
            try
            {
                if (string.Equals(name, MayorMandate.ToolName, StringComparison.Ordinal))
                {
                    MandateCallResult plan = m_Mandate.SetPlan(argumentsJson);
                    if (plan.Success)
                    {
                        m_OnPlanChanged();
                    }
                    return new ToolInvocationResult { Success = plan.Success, Text = plan.Text };
                }
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

        private void AppendToolImage(string toolName, string imagePath)
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
                string caption = string.Equals(toolName, "map_image", StringComparison.Ordinal)
                    ? "Map overview returned by the map_image tool."
                    : "Screenshot returned by the screenshot tool.";
                m_AppendHistory(new ChatMessage(ChatRole.User, new List<AIContent>
                {
                    new TextContent(caption),
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
