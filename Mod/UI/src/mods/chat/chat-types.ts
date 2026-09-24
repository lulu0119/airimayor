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

export type PlanPriority = "high" | "medium" | "low";
export type PlanStatus = "pending" | "in_progress" | "completed";

export interface PlanEntry {
  content: string;
  priority: PlanPriority;
  status: PlanStatus;
}

export interface AgentSnapshot {
  status: string;
  busy: boolean;
  pendingInputs: number;
  session: string;
  context?: AgentContextInfo;
  plan?: unknown;
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
  | "progress"
  | "plan";

export interface AgentWireEvent {
  kind: WireEventKind;
  text: string;
  tool?: string;
  status?: string;
  image?: string;
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
      state: ToolRowState;
    }
  | { id: number; kind: "error"; text: string };
