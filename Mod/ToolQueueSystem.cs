using System;
using System.Collections.Concurrent;
using airimayor.Host;
using Game;
using UnityEngine.Scripting;

namespace airimayor
{
    /// <summary>UIUpdate queue: drain work while paused (CS2MCP-style).</summary>
    public sealed partial class ToolQueueSystem : GameSystemBase
    {
        public static ToolQueueSystem Instance { get; private set; }

        private readonly ConcurrentQueue<Action> m_Pending = new ConcurrentQueue<Action>();

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            Instance = this;
            AgentTimeline.Info("tool-queue", "ToolQueueSystem created (UIUpdate)");
        }

        public void Enqueue(Action work)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            m_Pending.Enqueue(work);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            while (m_Pending.TryDequeue(out Action work))
            {
                try
                {
                    work();
                }
                catch (Exception exception)
                {
                    AgentTimeline.Warn("tool-queue", "work failed: " + exception);
                }
            }
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            base.OnDestroy();
        }
    }
}
