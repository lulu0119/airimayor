import type { AgentContextInfo } from "./chat-types";
import { useChatText } from "./locale";
import styles from "./chat.module.scss";

const formatTokenCount = (value: number): string => {
  if (value >= 1_000_000) {
    return `${(value / 1_000_000).toFixed(1)}M`;
  }
  if (value >= 1_000) {
    return `${Math.round(value / 1_000)}k`;
  }
  return `${Math.max(0, Math.round(value))}`;
};

const statusDotClass = (status: string, busy: boolean): string => {
  if (busy) {
    return status === "Thinking" ? styles.dotThinking : styles.dotRunning;
  }
  switch (status) {
    case "Error":
      return styles.dotError;
    case "Interrupted":
      return styles.dotInterrupted;
    default:
      return styles.dotDone;
  }
};

interface StatusBarProps {
  status: string;
  busy: boolean;
  pending: number;
  note: string;
  context: AgentContextInfo | null;
}

export const StatusBar = ({ status, busy, pending, note, context }: StatusBarProps) => {
  const text = useChatText();
  // The wire protocol keeps the English enum names; only the display is localized.
  const statusName = (value: string): string => {
    switch (value) {
      case "Idle":
        return text("Status.Idle", "Idle");
      case "Thinking":
        return text("Status.Thinking", "Thinking");
      case "Working":
        return text("Status.Working", "Working");
      case "Interrupted":
        return text("Status.Interrupted", "Interrupted");
      case "Error":
        return text("Status.Error", "Error");
      default:
        return value;
    }
  };
  const label = busy && note ? note : statusName(status);
  return (
    <div className={styles.statusBar}>
      <span className={`${styles.statusDot} ${statusDotClass(status, busy)}`} />
      <span className={styles.statusText}>{label}</span>
      {pending > 0 ? (
        <span className={styles.statusMeta}>{`${pending} ${text("Status.Queued", "queued")}`}</span>
      ) : null}
      {context ? (
        <span className={styles.statusMeta}>
          {`ctx ${formatTokenCount(context.estimatedTokens)}/${formatTokenCount(context.windowTokens)}${context.vision ? ` · ${text("Status.Vision", "vision")}` : ""}`}
        </span>
      ) : null}
    </div>
  );
};
