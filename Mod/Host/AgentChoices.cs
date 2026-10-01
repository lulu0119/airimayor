using System;

namespace airimayor.Host
{
    /// <summary>
    /// One Agent option. Built-in has no command. Another agent is another row.
    /// </summary>
    internal readonly struct AgentChoice
    {
        public readonly string Id;
        public readonly string English;
        public readonly string Chinese;
        public readonly string Command;
        public readonly string Arguments;

        public AgentChoice(string id, string english, string chinese, string command, string arguments)
        {
            Id = id;
            English = english;
            Chinese = chinese;
            Command = command;
            Arguments = arguments;
        }

        public bool IsBuiltIn => string.IsNullOrEmpty(Command);
    }

    internal static class AgentChoices
    {
        public const string BuiltIn = "built-in";

        public static readonly AgentChoice[] All = new[]
        {
            new AgentChoice(BuiltIn, "Built-in", "内置", null, null),
            new AgentChoice("opencode", "OpenCode", "OpenCode", "opencode", "acp"),
            new AgentChoice("codex", "Codex", "Codex", "codex-acp", null),
        };

        public static AgentChoice Find(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                foreach (AgentChoice choice in All)
                {
                    if (string.Equals(choice.Id, id, StringComparison.Ordinal))
                    {
                        return choice;
                    }
                }
            }
            return All[0];
        }
    }
}
