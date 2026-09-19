// Wire shapes from AgentUISystem plus the UI-side transcript model.
// The transcript is a flat append-only log; snapshots only hydrate it on
// session switch, live events append to it. See transcript.ts.

export type AgentStatusText = "Idle" | "Thinking" | "Working" | "Interrupted" | "Error";

export interface StateMessage {
  role: string;
  text: string;
  tool: string | null;
}

export interface AgentContextInfo {
  windowTokens: number;
  estimatedTokens: number;
  compactAtTokens: number;
  source: string;
  vision: boolean;
}

export interface AgentSnapshot {
  status: string;
  busy: boolean;
  pendingInputs: number;
  session: string;
  context?: AgentContextInfo;
  messages: StateMessage[];
}

export type WireEventKind =
  | "delta"
  | "tool"
  | "status"
  | "user"
  | "error"
  | "compact"
  | "turn"
  | "progress";

export interface AgentWireEvent {
  kind: WireEventKind;
  text: string;
  tool?: string;
  status?: string;
  steered?: boolean;
  image?: string;
  imageWidth?: number;
  imageHeight?: number;
}

export type ToolRowState = "running" | "done" | "error" | "interrupted";

export type ChatLine =
  | { id: number; kind: "user"; text: string }
  | { id: number; kind: "assistant"; text: string; streaming: boolean }
  | {
      id: number;
      kind: "tool";
      name: string;
      args: string;
      result: string | null;
      image: string | null;
      imageWidth: number | null;
      imageHeight: number | null;
      state: ToolRowState;
    }
  | { id: number; kind: "error"; text: string };
