using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace AgentRuntime
{
    /// <summary>
    /// Model-facing tool list for one round: host tools plus set_plan.
    /// Owns the plan tool end to end; the executor never names it.
    /// </summary>
    internal sealed class AgentToolSurface
    {
        private readonly IAgentTools m_Tools;
        private readonly ConversationBook m_Conversations;
        private readonly SessionPlan m_Plan;
        private readonly Action m_OnPlanChanged;

        public AgentToolSurface(
            IAgentTools tools,
            ConversationBook conversations,
            SessionPlan plan,
            Action onPlanChanged)
        {
            m_Tools = tools;
            m_Conversations = conversations;
            m_Plan = plan;
            m_OnPlanChanged = onPlanChanged ?? throw new ArgumentNullException(nameof(onPlanChanged));
        }

        public bool IsListed(string name, AgentModelProfile profile)
        {
            if (IsPlanTool(name))
            {
                return true;
            }
            if (IsTaskTool(name))
            {
                return true;
            }
            bool vision = profile != null && profile.VisionAvailable;
            foreach (AgentToolDeclaration tool in m_Tools.List(vision))
            {
                if (string.Equals(tool.Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        public List<AITool> Build(AgentModelProfile profile)
        {
            bool vision = profile != null && profile.VisionAvailable;
            var tools = new List<AITool>
            {
                AIFunctionFactory.CreateDeclaration(
                    SessionPlan.ToolName,
                    SessionPlan.ToolDescription,
                    SessionPlan.Parameters,
                    null),
            };
            if (m_Conversations != null)
            {
                foreach (AgentToolDeclaration sessionTool in m_Conversations.List())
                {
                    tools.Add(AIFunctionFactory.CreateDeclaration(
                        sessionTool.Name,
                        sessionTool.Description,
                        ParseParameters(sessionTool.ParametersJson),
                        null));
                }
            }
            foreach (AgentToolDeclaration tool in m_Tools.List(vision))
            {
                if (IsPlanTool(tool.Name))
                {
                    continue;
                }
                tools.Add(AIFunctionFactory.CreateDeclaration(
                    tool.Name,
                    tool.Description,
                    ParseParameters(tool.ParametersJson),
                    null));
            }
            return tools;
        }

        public Task<AgentToolResult> InvokeAsync(
            string name,
            string argumentsJson,
            CancellationToken cancellationToken)
        {
            if (IsPlanTool(name))
            {
                PlanCallResult plan = m_Plan.SetPlan(argumentsJson);
                if (plan.Success)
                {
                    m_OnPlanChanged();
                }
                return Task.FromResult(new AgentToolResult { Success = plan.Success, Text = plan.Text });
            }
            if (IsTaskTool(name))
            {
                return m_Conversations.InvokeAsync(name, argumentsJson, cancellationToken);
            }
            return m_Tools.InvokeAsync(name, argumentsJson, cancellationToken);
        }

        private static bool IsPlanTool(string name)
        {
            return string.Equals(name, SessionPlan.ToolName, StringComparison.Ordinal);
        }

        private bool IsTaskTool(string name)
        {
            return m_Conversations != null &&
                string.Equals(name, ConversationBook.TaskName, StringComparison.Ordinal);
        }

        private static JsonElement ParseParameters(string parametersJson)
        {
            using (JsonDocument document = JsonDocument.Parse(parametersJson))
            {
                return document.RootElement.Clone();
            }
        }
    }
}
