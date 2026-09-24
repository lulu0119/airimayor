using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentRuntime;
using Microsoft.Extensions.AI;
using Xunit;

namespace CitiesSkylines2Agent.Agent
{
    public sealed class AgentRuntimeTests
    {
        [Fact]
        public async Task Prompt_records_the_player_line()
        {
            using (AgentRuntime.AgentRuntime runtime = Open(
                new QuietTools(),
                new ScriptedChatClient(Text("Noted."))))
            {
                runtime.Prompt("hello");
                await WaitUntilAsync(() => runtime.Status == AgentStatus.Idle && runtime.ChatStateJson().Contains("hello"));
                using (JsonDocument state = JsonDocument.Parse(runtime.ChatStateJson()))
                {
                    Assert.Contains("hello", state.RootElement.GetProperty("messages").ToString());
                }
            }
        }

        [Fact]
        public async Task Player_message_waits_for_the_in_flight_call_and_keeps_the_plan()
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tools = new HoldingTools(started);
            var client = new ScriptedChatClient(
                ToolCall("plan-1", "set_plan", new Dictionary<string, object>
                {
                    ["goal"] = "keep the lights on",
                    ["success"] = "power stable",
                }),
                ToolCall("hold-1", "hold", new Dictionary<string, object>()),
                Text("Done."));
            using (AgentRuntime.AgentRuntime runtime = Open(tools, client))
            {
                runtime.Prompt("start");
                Assert.True(await started.Task.WaitAsync(TimeSpan.FromSeconds(5)));
                runtime.Prompt("steer");
                await Task.Delay(100);
                Assert.False(tools.Cancelled);
                Assert.Equal(AgentStatus.Working, runtime.Status);
                tools.Release();
                await WaitUntilAsync(() => runtime.Status == AgentStatus.Idle && runtime.ChatStateJson().Contains("steer"));
                using (JsonDocument state = JsonDocument.Parse(runtime.ChatStateJson()))
                {
                    JsonElement plan = state.RootElement.GetProperty("plan");
                    Assert.Equal("keep the lights on", plan.GetProperty("goal").GetString());
                    Assert.Equal("power stable", plan.GetProperty("success").GetString());
                }
                Assert.False(tools.Cancelled);
            }
        }

        [Fact]
        public async Task Cancel_stops_the_in_flight_call_and_keeps_the_plan()
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tools = new HoldingTools(started);
            var client = new ScriptedChatClient(
                ToolCall("plan-1", "set_plan", new Dictionary<string, object>
                {
                    ["goal"] = "keep the lights on",
                    ["success"] = "power stable",
                }),
                ToolCall("hold-1", "hold", new Dictionary<string, object>()));
            using (AgentRuntime.AgentRuntime runtime = Open(tools, client))
            {
                runtime.Prompt("start");
                Assert.True(await started.Task.WaitAsync(TimeSpan.FromSeconds(5)));
                runtime.Cancel();
                await WaitUntilAsync(() => runtime.Status == AgentStatus.Idle && !runtime.IsBusy);
                using (JsonDocument state = JsonDocument.Parse(runtime.ChatStateJson()))
                {
                    JsonElement plan = state.RootElement.GetProperty("plan");
                    Assert.Equal("keep the lights on", plan.GetProperty("goal").GetString());
                }
                Assert.True(tools.Cancelled);
            }
        }

        private static AgentRuntime.AgentRuntime Open(IAgentTools tools, IChatClient client)
        {
            return new AgentRuntime.AgentRuntime(
                tools,
                "instructions",
                () => new ModelSettings
                {
                    Model = "test",
                    WindowTokens = 32_000,
                    Wire = ModelWire.ChatCompletions,
                },
                null,
                client);
        }

        private static async Task WaitUntilAsync(Func<bool> ready)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!ready())
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("runtime did not settle");
                }
                await Task.Delay(20);
            }
        }

        private static IReadOnlyList<ChatResponseUpdate> Text(string text)
        {
            return new[]
            {
                new ChatResponseUpdate(ChatRole.Assistant, text),
            };
        }

        private static IReadOnlyList<ChatResponseUpdate> ToolCall(
            string callId,
            string name,
            IDictionary<string, object> arguments)
        {
            return new[]
            {
                new ChatResponseUpdate(ChatRole.Assistant, new List<AIContent>
                {
                    new FunctionCallContent(callId, name, arguments),
                }),
            };
        }

        private sealed class QuietTools : IAgentTools
        {
            public IReadOnlyList<AgentToolDeclaration> List(bool visionAvailable)
            {
                return Array.Empty<AgentToolDeclaration>();
            }

            public Task<AgentToolResult> InvokeAsync(
                string name,
                string argumentsJson,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(new AgentToolResult { Success = true, Text = "{}" });
            }
        }

        private sealed class HoldingTools : IAgentTools
        {
            private readonly TaskCompletionSource<bool> m_Started;
            private readonly TaskCompletionSource<bool> m_Release =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public HoldingTools(TaskCompletionSource<bool> started)
            {
                m_Started = started;
            }

            public bool Cancelled { get; private set; }

            public void Release()
            {
                m_Release.TrySetResult(true);
            }

            public IReadOnlyList<AgentToolDeclaration> List(bool visionAvailable)
            {
                return new[]
                {
                    new AgentToolDeclaration
                    {
                        Name = "hold",
                        Description = "block until released",
                        ParametersJson = "{\"type\":\"object\",\"properties\":{}}",
                    },
                };
            }

            public async Task<AgentToolResult> InvokeAsync(
                string name,
                string argumentsJson,
                CancellationToken cancellationToken)
            {
                m_Started.TrySetResult(true);
                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
                {
                    Task finished = await Task.WhenAny(m_Release.Task, cancelled.Task);
                    if (finished == cancelled.Task)
                    {
                        Cancelled = true;
                        throw new OperationCanceledException(cancellationToken);
                    }
                }
                return new AgentToolResult { Success = true, Text = "{}" };
            }
        }

        private sealed class ScriptedChatClient : IChatClient
        {
            private readonly Queue<IReadOnlyList<ChatResponseUpdate>> m_Scripts;

            public ScriptedChatClient(params IReadOnlyList<ChatResponseUpdate>[] scripts)
            {
                m_Scripts = new Queue<IReadOnlyList<ChatResponseUpdate>>(scripts);
            }

            public ChatClientMetadata Metadata => new ChatClientMetadata("script");

            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions options = null,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions options = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                if (m_Scripts.Count == 0)
                {
                    yield return new ChatResponseUpdate(ChatRole.Assistant, "idle");
                    yield break;
                }
                foreach (ChatResponseUpdate update in m_Scripts.Dequeue())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return update;
                    await Task.Yield();
                }
            }

            public object GetService(Type serviceType, object serviceKey = null)
            {
                return null;
            }

            public void Dispose()
            {
            }
        }
    }
}
