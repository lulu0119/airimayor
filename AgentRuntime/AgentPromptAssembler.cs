using System;
using System.Collections.Generic;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace AgentRuntime
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

        public void Apply(List<ChatMessage> history, string liveNote)
        {
            if (history == null) return;
            EnsureSystemPrompt(history);
            EnsureLiveNote(history, liveNote);
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

        private void EnsureLiveNote(List<ChatMessage> history, string liveNote)
        {
            for (int index = history.Count - 1; index >= 0; index--)
            {
                ChatMessage message = history[index];
                if (message.Role != ChatRole.System ||
                    !(message.Text ?? "").StartsWith(SessionPlan.HistoryNotePrefix, StringComparison.Ordinal))
                {
                    continue;
                }
                history.RemoveAt(index);
            }
            if (string.IsNullOrEmpty(liveNote))
            {
                return;
            }

            int insertAt = 1;
            if (history.Count > 1 &&
                history[1].Role == ChatRole.System &&
                (history[1].Text ?? "").StartsWith(SummaryPrefix, StringComparison.Ordinal))
            {
                insertAt = 2;
            }
            history.Insert(insertAt, new ChatMessage(ChatRole.System, liveNote));
        }
    }
}
