// Single owner of chat view state. Rule: a fresh mount hydrates the
// transcript from the state snapshot; afterwards lines only grow via live
// events, same-session snapshots only patch the chrome (status/busy/pending/
// context/plan). This keeps the two C# sources from diverging in the UI.

import { useEffect, useRef, useState } from "react";
import { useValue } from "cs2/api";
import { agentEvents$, agentState$, interruptTurn, sendChatMessage } from "mods/bindings";
import type { AgentContextInfo, AgentSnapshot, AgentWireEvent, ChatLine, MayorPlan } from "./chat-types";
import type { RunningTool } from "./send-effect";
import { applyWireEvent, emptyTranscript, hydrateTranscript } from "./transcript";

export interface QueuedMessage {
  key: number;
  text: string;
}

export interface ChatModel {
  session: string;
  lines: ChatLine[];
  queued: QueuedMessage[];
  status: string;
  runningTool: RunningTool | null;
  busy: boolean;
  pending: number;
  note: string;
  context: AgentContextInfo | null;
  plan: MayorPlan | null;
  send: (text: string) => void;
  interrupt: () => void;
}

const isActiveStatus = (status: string): boolean =>
  status === "Thinking" || status === "Working";

const parseSnapshot = (json: string): AgentSnapshot | null => {
  try {
    const parsed = JSON.parse(json) as AgentSnapshot;
    if (!parsed || typeof parsed !== "object" || !Array.isArray(parsed.messages)) {
      return null;
    }
    return parsed;
  } catch {
    return null;
  }
};

const parsePlan = (value: unknown): MayorPlan | null => {
  if (typeof value === "string") {
    try {
      value = JSON.parse(value);
    } catch {
      return null;
    }
  }
  if (value == null || typeof value !== "object") {
    return null;
  }
  const record = value as { goal?: unknown; success?: unknown };
  if (typeof record.goal !== "string" || record.goal.length === 0) {
    return null;
  }
  return {
    goal: record.goal,
    success: typeof record.success === "string" ? record.success : "",
  };
};

const parseEvent = (json: string): AgentWireEvent | null => {
  try {
    const parsed = JSON.parse(json) as AgentWireEvent;
    if (!parsed || typeof parsed.kind !== "string") {
      return null;
    }
    return parsed;
  } catch {
    return null;
  }
};

export const useChat = (): ChatModel => {
  const [session, setSession] = useState("");
  const [lines, setLines] = useState<ChatLine[]>(emptyTranscript.lines);
  const [queued, setQueued] = useState<QueuedMessage[]>([]);
  const [status, setStatus] = useState("Idle");
  const [runningTool, setRunningTool] = useState<RunningTool | null>(null);
  const [busy, setBusy] = useState(false);
  const [pending, setPending] = useState(0);
  const [note, setNote] = useState("");
  const [context, setContext] = useState<AgentContextInfo | null>(null);
  const [plan, setPlan] = useState<MayorPlan | null>(null);
  const sessionRef = useRef("");
  const nextIdRef = useRef(0);
  const runningRef = useRef<RunningTool | null>(null);
  const queuedRef = useRef<QueuedMessage[]>([]);
  const snapshotJson = useValue(agentState$);
  const subscribed = useRef(false);
  const rememberRunning = (next: RunningTool | null) => {
    runningRef.current = next;
    setRunningTool(next);
  };

  useEffect(() => {
    if (subscribed.current) {
      return;
    }
    subscribed.current = true;
    const subscription = agentEvents$.subscribe((json) => {
      const event = parseEvent(json);
      if (!event) {
        return;
      }
      setLines((current) => {
        const next = applyWireEvent({ lines: current, nextId: nextIdRef.current }, event);
        nextIdRef.current = next.nextId;
        return next.lines;
      });
      switch (event.kind) {
        case "user":
          setBusy(true);
          setNote("");
          break;
        case "delta":
          setBusy(true);
          break;
        case "tool":
          setBusy(true);
          if (runningRef.current) {
            rememberRunning(null);
          } else {
            rememberRunning({
              name: event.tool ?? "tool",
              args: event.text ?? "",
            });
          }
          break;
        case "status": {
          const next = event.status ?? event.text;
          setStatus(next);
          setBusy(isActiveStatus(next));
          if (next !== "Working") {
            rememberRunning(null);
          }
          if (next === "Idle" || next === "Interrupted" || next === "Error") {
            setNote("");
          }
          break;
        }
        case "progress":
          setNote(event.text);
          break;
        case "error":
          setBusy(false);
          rememberRunning(null);
          break;
        case "turn":
          setBusy(false);
          setNote("");
          rememberRunning(null);
          break;
        case "compact":
          break;
        case "plan":
          setPlan(parsePlan(event.text));
          break;
      }
    });
    return () => {
      subscribed.current = false;
      subscription.dispose();
    };
  }, []);

  useEffect(() => {
    const snapshot = parseSnapshot(snapshotJson);
    if (!snapshot) {
      return;
    }
      if (!snapshot.session) {
      if (sessionRef.current) {
        sessionRef.current = "";
        nextIdRef.current = 0;
        queuedRef.current = [];
        setSession("");
        setQueued([]);
        setLines([]);
        setStatus("Idle");
        rememberRunning(null);
        setBusy(false);
        setPending(0);
        setNote("");
        setContext(null);
        setPlan(null);
      }
      return;
    }
    if (snapshot.session !== sessionRef.current) {
      sessionRef.current = snapshot.session;
      const hydrated = hydrateTranscript(snapshot.messages ?? []);
      nextIdRef.current = hydrated.nextId;
      setSession(snapshot.session);
      setStatus(snapshot.status);
      rememberRunning(null);
      setBusy(snapshot.busy);
      setPending(snapshot.pendingInputs ?? 0);
      setContext(snapshot.context ?? null);
      setPlan(parsePlan(snapshot.plan));
      setLines(hydrated.lines);
      return;
    }
    setStatus(snapshot.status);
    if (snapshot.status !== "Working") {
      rememberRunning(null);
    }
    setBusy(snapshot.busy);
    setPending(snapshot.pendingInputs ?? 0);
    setContext(snapshot.context ?? null);
    setPlan(parsePlan(snapshot.plan));
  }, [snapshotJson]);

  return {
    session,
    lines,
    queued,
    status,
    runningTool,
    busy,
    pending,
    note,
    context,
    plan,
    send: (text: string) => {
      if (text.trim().length === 0 || !sessionRef.current) {
        return;
      }
      sendChatMessage(text);
      setBusy(true);
    },
    interrupt: () => {
      interruptTurn();
    },
  };
};
