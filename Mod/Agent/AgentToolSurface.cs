using System;
using System.Collections.Generic;
using Microsoft.Extensions.AI;

namespace CitiesSkylines2Agent.Agent
{
    /// <summary>
    /// Owns the model-facing tool surface for a round: catalog tools,
    /// filtered by vision capability and settings.
    /// </summary>
    internal sealed class AgentToolSurface
    {
        private static readonly HashSet<string> s_DevelopmentTools =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "replace_road_type", "debug_zone_blocks", "save_game",
            };

        public bool IsAvailable(string name, AgentModelProfile profile)
        {
            return IsAllowed(name, profile != null && profile.VisionAvailable);
        }

        public List<AITool> Build(AgentModelProfile profile)
        {
            bool visionAvailable = profile != null && profile.VisionAvailable;
            var tools = new List<AITool>();
            foreach (ToolDefinition tool in ToolCatalog.Tools)
            {
                if (!IsAllowed(tool.Name, visionAvailable))
                {
                    continue;
                }
                tools.Add(AIFunctionFactory.CreateDeclaration(
                    tool.Name,
                    tool.Description,
                    tool.Parameters,
                    null));
            }
            return tools;
        }

        private static bool IsAllowed(string name, bool visionAvailable)
        {
            if (!visionAvailable &&
                (name == "screenshot" || name == "get_camera" || name == "set_camera"))
            {
                return false;
            }
            if (s_DevelopmentTools.Contains(name))
            {
                return Setting.StaticEnableDevelopmentTools;
            }
            if (name == "purchase_development_node" &&
                !Setting.StaticAllowProgressionPurchases)
            {
                return false;
            }
            return name != "demolish" || Setting.StaticAllowDemolition;
        }
    }
}
