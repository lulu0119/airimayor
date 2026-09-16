// Single owner of chat view state. Rule: a fresh mount hydrates the
// transcript from the state snapshot; afterwards lines only grow via live
// events, same-session snapshots only patch the chrome (status/busy/pending/
// context). This keeps the two C# sources from diverging in the UI.

import { useEffect, useRef, useState } from "react";
import { useValue } from "cs2/api";
import { agentEvents$, agentState$, interruptTurn, sendChatMessage } from "mods/bindings";
import type { AgentContextInfo, AgentSnapshot, AgentWireEvent, ChatLine } from "./chat-types";
import { applyWireEvent, emptyTranscript, hydrateTranscript } from "./transcript";

export interface ChatModel {
  session: string;
  lines: ChatLine[];
  status: string;
  busy: boolean;
  pending: number;
  note: string;
  context: AgentContextInfo | null;
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
  const [status, setStatus] = useState("Idle");
  const [busy, setBusy] = useState(false);
  const [pending, setPending] = useState(0);
  const [note, setNote] = useState("");
  const [context, setContext] = useState<AgentContextInfo | null>(null);
  const sessionRef = useRef("");
  const nextIdRef = useRef(0);
  const snapshotJson = useValue(agentState$);
  const subscribed = useRef(false);

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
        case "tool":
          setBusy(true);
          break;
        case "status": {
          const next = event.status ?? event.text;
          setStatus(next);
          setBusy(isActiveStatus(next));
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
          break;
        case "turn":
          setBusy(false);
          setNote("");
          break;
        case "compact":
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
        setSession("");
        setLines([]);
        setStatus("Idle");
        setBusy(false);
        setPending(0);
        setNote("");
        setContext(null);
      }
      return;
    }
    if (snapshot.session !== sessionRef.current) {
      sessionRef.current = snapshot.session;
      const hydrated = hydrateTranscript(snapshot.messages ?? []);
      nextIdRef.current = hydrated.nextId;
      setSession(snapshot.session);
      setStatus(snapshot.status);
      setBusy(snapshot.busy);
      setPending(snapshot.pendingInputs ?? 0);
      setContext(snapshot.context ?? null);
      setLines(hydrated.lines);
      return;
    }
    setStatus(snapshot.status);
    setBusy(snapshot.busy);
    setPending(snapshot.pendingInputs ?? 0);
    setContext(snapshot.context ?? null);
  }, [snapshotJson]);

  return {
    session,
    lines,
    status,
    busy,
    pending,
    note,
    context,
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
