using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentRuntime;

namespace CitiesSkylines2Agent.Host
{
    /// <summary>
    /// City tools behind <see cref="IAgentTools"/>. Permission filtering
    /// lives here; the runtime never names city tools.
    /// </summary>
    internal sealed class Cs2AgentTools : IAgentTools
    {
        private static readonly HashSet<string> s_DevelopmentTools =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "replace_road_type", "debug_zone_blocks", "save_game",
            };

        private static readonly HashSet<string> s_VisionTools =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "screenshot", "get_camera", "set_camera", "map_image",
            };

        public IReadOnlyList<AgentToolDeclaration> List(bool visionAvailable)
        {
            var listed = new List<AgentToolDeclaration>();
            foreach (ToolDefinition tool in ToolCatalog.Tools)
            {
                if (!IsAllowed(tool.Name, visionAvailable))
                {
                    continue;
                }
                listed.Add(new AgentToolDeclaration
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    ParametersJson = tool.Parameters.GetRawText(),
                });
            }
            return listed;
        }

        public async Task<AgentToolResult> InvokeAsync(
            string name,
            string argumentsJson,
            CancellationToken cancellationToken)
        {
            ToolDefinition tool = ToolCatalog.Find(name);
            if (tool == null)
            {
                return new AgentToolResult
                {
                    Success = false,
                    Text = JsonSerializer.Serialize(new { error = "unknown tool: " + name }),
                };
            }
            ToolInvocationResult invoked = await AgentToolBridge.InvokeAsync(
                tool, argumentsJson, cancellationToken);
            byte[] image = invoked.ImagePng;
            return new AgentToolResult
            {
                Success = invoked.Success,
                Text = invoked.Text,
                ImagePng = image,
                ImageCaption = ImageCaption(name),
                PreviewJpeg = invoked.PreviewBytes,
            };
        }

        private static string ImageCaption(string name)
        {
            if (string.Equals(name, "map_image", StringComparison.Ordinal))
            {
                return "Map overview returned by the map_image tool.";
            }
            if (string.Equals(name, "screenshot", StringComparison.Ordinal))
            {
                return "Screenshot returned by the screenshot tool.";
            }
            return null;
        }

        private static bool IsAllowed(string name, bool visionAvailable)
        {
            if (!visionAvailable && s_VisionTools.Contains(name))
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
