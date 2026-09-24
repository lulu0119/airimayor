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
    /// </summary>
    internal sealed class AgentToolSurface
    {
        private readonly IAgentTools m_Tools;

        public AgentToolSurface(IAgentTools tools)
        {
            m_Tools = tools;
        }

        public bool IsListed(string name, AgentModelProfile profile)
        {
            if (string.Equals(name, SessionPlan.ToolName, StringComparison.Ordinal))
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
            foreach (AgentToolDeclaration tool in m_Tools.List(vision))
            {
                if (string.Equals(tool.Name, SessionPlan.ToolName, StringComparison.Ordinal))
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
            return m_Tools.InvokeAsync(name, argumentsJson, cancellationToken);
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
