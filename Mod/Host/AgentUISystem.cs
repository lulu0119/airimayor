using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using AgentRuntime;
using Colossal.Serialization.Entities;
using Colossal.UI.Binding;
using Game;
using Game.SceneFlow;
using Game.UI;
using UnityEngine.Scripting;

namespace airimayor.Host
{
    /// <summary>
    /// Bridges the agent loop to Gameface: publishes agent state and a live
    /// event stream (deltas, tool cards, status), and receives player commands
    /// (send / interrupt). Registered at UIUpdate so it keeps running while the
    /// simulation is paused.
    /// </summary>
    public sealed partial class AgentUISystem : UISystemBase
    {
        private const string Group = "airimayor";
        private const int MaxEventsPerUpdate = 32;

        private readonly ConcurrentQueue<SessionUpdate> m_Events =
            new ConcurrentQueue<SessionUpdate>();
        private ValueBinding<string> m_StateBinding;
        private EventBinding<string> m_EventBinding;
        private int m_StateDirty;
        private AgentRuntime.AgentRuntime m_Session;
        private SessionUpdate m_DeferredEvent;
        private bool m_AutoStartSent;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();

            m_StateBinding = new ValueBinding<string>(
                Group,
                "state",
                "{}",
                ValueWriters.Create<string>(),
                System.Collections.Generic.EqualityComparer<string>.Default);
            AddBinding(m_StateBinding);

            m_EventBinding = new EventBinding<string>(Group, "event", ValueWriters.Create<string>());
            AddBinding(m_EventBinding);

            AddBinding(new TriggerBinding<string>(
                Group,
                "send",
                OnSend,
                ValueReaders.Create<string>()));
            AddBinding(new TriggerBinding(Group, "interrupt", OnInterrupt));

            PushState();
        }

        [Preserve]
        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (mode != GameMode.Game)
            {
                LeaveGameSession();
                return;
            }

            CloseSession();
            OpenSession();
            while (m_Events.TryDequeue(out _)) { }
            m_DeferredEvent = null;
            m_AutoStartSent = false;
            Interlocked.Exchange(ref m_StateDirty, 1);
            PushState();
        }

        private void LeaveGameSession()
        {
            CloseSession();
            while (m_Events.TryDequeue(out _)) { }
            m_DeferredEvent = null;
            m_AutoStartSent = false;
            Interlocked.Exchange(ref m_StateDirty, 1);
            PushState();
        }

        private static bool IsInLoadedCity()
        {
            GameManager manager = GameManager.instance;
            return manager != null &&
                   manager.gameMode == GameMode.Game &&
                   !manager.isGameLoading;
        }

        private void OnSend(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || !IsInLoadedCity())
            {
                return;
            }
            m_Session?.Prompt(text);
        }

        private void OnInterrupt()
        {
            if (!IsInLoadedCity())
            {
                return;
            }
            m_Session?.Cancel();
        }

        private void OpenSession()
        {
            m_Session = AgentSessionHost.Current = new AgentRuntime.AgentRuntime(
                new Cs2AgentTools(),
                AgentSystemPrompt.Text,
                ReadModel,
                ModPaths.LogsDirectory,
                message => Mod.log.Warn(message));
            m_Session.Updated += OnAgentEvent;
            Mod.log.Info("agent session opened " + m_Session.Timeline.SessionId);
        }

        private static ModelSettings ReadModel()
        {
            return new ModelSettings
            {
                Endpoint = Setting.StaticEndpoint,
                ApiKey = Setting.StaticApiKey,
                Model = Setting.StaticModel,
                WindowTokens = Setting.StaticWindowTokens,
                Vision = Setting.StaticVisionToolMode == VisionToolMode.On,
                ContinueWhenIdle = Setting.StaticContinuous,
                Wire = Setting.StaticApiKind == ApiKind.Responses
                    ? ModelWire.Responses
                    : ModelWire.ChatCompletions,
            };
        }

        private void CloseSession()
        {
            if (m_Session == null)
            {
                return;
            }
            m_Session.Updated -= OnAgentEvent;
            Mod.log.Info("agent session closed " + m_Session.Timeline.SessionId);
            if (ReferenceEquals(AgentSessionHost.Current, m_Session))
            {
                AgentSessionHost.Current = null;
            }
            m_Session.Dispose();
            m_Session = null;
        }

        private void OnAgentEvent(SessionUpdate agentEvent)
        {
            if (agentEvent == null)
            {
                return;
            }
            m_Events.Enqueue(agentEvent);
            if (NeedsStateSnapshot(agentEvent))
            {
                Interlocked.Exchange(ref m_StateDirty, 1);
            }
        }

        private static bool NeedsStateSnapshot(SessionUpdate agentEvent)
        {
            if (agentEvent.Kind == "user" ||
                agentEvent.Kind == "error" ||
                agentEvent.Kind == "turn" ||
                agentEvent.Kind == "compact" ||
                agentEvent.Kind == "tool" ||
                agentEvent.Kind == "delta" ||
                agentEvent.Kind == "plan")
            {
                return true;
            }
            if (agentEvent.Kind != "status")
            {
                return false;
            }
            return agentEvent.Status == AgentStatus.Idle ||
                   agentEvent.Status == AgentStatus.Interrupted ||
                   agentEvent.Status == AgentStatus.Error;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            base.OnUpdate();
            TryAutoStart();
            int processed = 0;
            while (processed < MaxEventsPerUpdate && TryDequeueForUi(out SessionUpdate agentEvent))
            {
                m_EventBinding.Trigger(agentEvent.ToJsonString());
                processed++;
            }
            if (Interlocked.Exchange(ref m_StateDirty, 0) == 1)
            {
                PushState();
            }
        }

        private void TryAutoStart()
        {
            GameManager manager = GameManager.instance;
            if (manager == null || manager.gameMode != GameMode.Game || manager.isGameLoading)
            {
                m_AutoStartSent = false;
                return;
            }
            if (m_AutoStartSent || !Setting.StaticAutoStart)
            {
                return;
            }
            if (m_Session == null)
            {
                return;
            }
            m_AutoStartSent = true;
            m_Session.Prompt(Setting.StaticStartupPrompt);
        }

        private bool TryDequeueForUi(out SessionUpdate agentEvent)
        {
            if (m_DeferredEvent != null)
            {
                agentEvent = m_DeferredEvent;
                m_DeferredEvent = null;
            }
            else if (!m_Events.TryDequeue(out agentEvent))
            {
                return false;
            }

            if (agentEvent.Kind != "delta")
            {
                return true;
            }

            var text = new StringBuilder(agentEvent.Text ?? "");
            while (m_Events.TryDequeue(out SessionUpdate next))
            {
                if (next.Kind != "delta")
                {
                    m_DeferredEvent = next;
                    break;
                }
                text.Append(next.Text ?? "");
            }

            agentEvent = new SessionUpdate
            {
                Kind = "delta",
                Text = text.ToString(),
            };
            return true;
        }

        private void PushState()
        {
            string json = m_Session == null ? "{}" : m_Session.ChatStateJson();
            if (m_StateBinding.value != json)
            {
                m_StateBinding.Update(json);
            }
        }

        [Preserve]
        protected override void OnDestroy()
        {
            CloseSession();
            base.OnDestroy();
        }
    }
}
