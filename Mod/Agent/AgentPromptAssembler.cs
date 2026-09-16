using System;
using System.Collections.Generic;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace CitiesSkylines2Agent.Agent
{
    internal sealed class AgentPromptAssembler
    {
        public AgentPromptAssembler(string systemPrompt, string summaryPrefix)
        {
            SystemPrompt = systemPrompt;
            SummaryPrefix = summaryPrefix;
        }

        public string SystemPrompt { get; }
        public string SummaryPrefix { get; }

        public void Apply(List<ChatMessage> history)
        {
            if (history == null) return;
            EnsureSystemPrompt(history);
        }

        public void Rebuild(
            List<ChatMessage> history,
            string summary,
            List<ChatMessage> keptMessages)
        {
            history.Clear();
            history.Add(new ChatMessage(ChatRole.System, SystemPrompt));
            history.Add(new ChatMessage(ChatRole.System, SummaryPrefix + summary));
            history.AddRange(keptMessages);
            Apply(history);
        }

        private void EnsureSystemPrompt(List<ChatMessage> history)
        {
            int promptIndex = -1;
            for (int index = history.Count - 1; index >= 0; index--)
            {
                ChatMessage message = history[index];
                if (message.Role != ChatRole.System || !string.Equals(message.Text, SystemPrompt, StringComparison.Ordinal)) continue;
                if (promptIndex < 0) promptIndex = index;
                else { history.RemoveAt(index); promptIndex--; }
            }
            if (promptIndex < 0)
            {
                history.Insert(0, new ChatMessage(ChatRole.System, SystemPrompt));
            }
            else if (promptIndex != 0)
            {
                ChatMessage prompt = history[promptIndex];
                history.RemoveAt(promptIndex);
                history.Insert(0, prompt);
            }
        }
    }
}
