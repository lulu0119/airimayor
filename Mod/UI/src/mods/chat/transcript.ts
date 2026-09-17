// Pure transcript reducer: the only place ChatLine[] is built.
// No React, no bindings, no window globals — snapshots hydrate on session
// switch, live wire events append. Tool start/done pairing relies on arrival
// order: the executor emits start then done per call, sequentially.

import type { AgentWireEvent, ChatLine, StateMessage, ToolRowState } from "./chat-types";

export interface Transcript {
  lines: ChatLine[];
  nextId: number;
}

export const emptyTranscript: Transcript = { lines: [], nextId: 0 };

const maxTextLength = 4000;

const truncate = (text: string): string =>
  text.length > maxTextLength ? text.slice(0, maxTextLength) + "…" : text;

const push = (
  transcript: Transcript,
  lines: ChatLine[],
  make: (id: number) => ChatLine,
): Transcript => ({
  lines: [...lines, make(transcript.nextId)],
  nextId: transcript.nextId + 1,
});

const finalizeStreaming = (lines: ChatLine[]): ChatLine[] =>
  lines.map((line) =>
    line.kind === "assistant" && line.streaming ? { ...line, streaming: false } : line,
  );

const settleRunningTools = (lines: ChatLine[], state: ToolRowState): ChatLine[] =>
  lines.map((line) =>
    line.kind === "tool" && line.state === "running" ? { ...line, state } : line,
  );

export function hydrateTranscript(messages: StateMessage[]): Transcript {
  const lines: ChatLine[] = [];
  messages.forEach((message) => {
    const text = (message.text ?? "").trim();
    if (message.role === "tool") {
      lines.push({
        id: lines.length,
        kind: "tool",
        name: message.tool ?? "tool",
        args: "",
        result: text.length > 0 ? text : null,
        image: null,
        state: "done",
      });
      return;
    }
    if (text.length === 0) {
      return;
    }
    if (message.role === "user") {
      lines.push({ id: lines.length, kind: "user", text });
    } else if (message.role === "error") {
      lines.push({ id: lines.length, kind: "error", text });
    } else {
      lines.push({ id: lines.length, kind: "assistant", text, streaming: false });
    }
  });
  return { lines, nextId: lines.length };
}

export function applyWireEvent(
  transcript: Transcript,
  event: AgentWireEvent,
): Transcript {
  const text = event.text ?? "";
  switch (event.kind) {
    case "user": {
      if (text.trim().length === 0) {
        return transcript;
      }
      const settled = settleRunningTools(finalizeStreaming(transcript.lines), "done");
      return push(transcript, settled, (id) => ({ id, kind: "user", text }));
    }
    case "delta": {
      if (text.length === 0) {
        return transcript;
      }
      const lines = [...transcript.lines];
      const last = lines[lines.length - 1];
      if (last && last.kind === "assistant" && last.streaming) {
        lines[lines.length - 1] = { ...last, text: truncate(last.text + text) };
        return { ...transcript, lines };
      }
      return push(transcript, lines, (id) => ({
        id,
        kind: "assistant",
        text: truncate(text),
        streaming: true,
      }));
    }
    case "tool": {
      const lines = [...transcript.lines];
      const last = lines[lines.length - 1];
      if (last && last.kind === "tool" && last.state === "running") {
        lines[lines.length - 1] = {
          ...last,
          result: text.length > 0 ? truncate(text) : null,
          image: event.image ? event.image : null,
          state: event.status === "Error" ? "error" : "done",
        };
        return { ...transcript, lines };
      }
      return push(transcript, lines, (id) => ({
        id,
        kind: "tool",
        name: event.tool ?? "tool",
        args: truncate(text),
        result: null,
        image: null,
        state: "running",
      }));
    }
    case "status": {
      const status = event.status ?? "";
      if (status === "Idle" || status === "Interrupted" || status === "Error") {
        return {
          ...transcript,
          lines: settleRunningTools(
            finalizeStreaming(transcript.lines),
            status === "Interrupted" ? "interrupted" : "done",
          ),
        };
      }
      return transcript;
    }
    case "turn": {
      return {
        ...transcript,
        lines: settleRunningTools(finalizeStreaming(transcript.lines), "done"),
      };
    }
    case "error": {
      const settled = settleRunningTools(finalizeStreaming(transcript.lines), "error");
      if (text.trim().length === 0) {
        return { ...transcript, lines: settled };
      }
      return push(transcript, settled, (id) => ({ id, kind: "error", text }));
    }
    case "progress":
    case "compact":
    default: {
      return transcript;
    }
  }
}
