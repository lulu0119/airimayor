import { useState } from "react";
import type { ChatLine, ToolRowState } from "./chat-types";
import styles from "./chat.module.scss";

const toolStateLabel: Record<ToolRowState, string> = {
  running: "Running",
  done: "Done",
  error: "Error",
  interrupted: "Interrupted",
};

const toolDotClass = (state: ToolRowState): string => {
  switch (state) {
    case "running":
      return styles.dotRunning;
    case "done":
      return styles.dotDone;
    case "error":
      return styles.dotError;
    case "interrupted":
      return styles.dotInterrupted;
  }
};

const roleClass = (kind: ChatLine["kind"]): string => {
  switch (kind) {
    case "user":
      return styles.userRow;
    case "error":
      return styles.errorRow;
    default:
      return styles.assistantRow;
  }
};

const roleLabel = (kind: ChatLine["kind"]): string => {
  switch (kind) {
    case "user":
      return "You";
    case "error":
      return "Error";
    default:
      return "Mayor";
  }
};

const ToolRow = ({ line }: { line: Extract<ChatLine, { kind: "tool" }> }) => {
  const [expanded, setExpanded] = useState(false);
  return (
    <div className={styles.toolRow}>
      <div
        className={styles.toolHeader}
        onClick={() => setExpanded(!expanded)}
      >
        <span className={`${styles.toolDot} ${toolDotClass(line.state)}`} />
        <span className={styles.toolName}>{line.name}</span>
        <span className={styles.toolState}>{toolStateLabel[line.state]}</span>
        <span className={styles.toolChevron}>{expanded ? "▾" : "▸"}</span>
      </div>
      {expanded ? (
        <div className={styles.toolBody}>
          {line.args ? (
            <>
              <div className={styles.toolSectionLabel}>Arguments</div>
              <pre className={styles.toolPre}>{line.args}</pre>
            </>
          ) : null}
          <div className={styles.toolSectionLabel}>Result</div>
          <pre className={styles.toolPre}>{line.result ?? "No result yet."}</pre>
        </div>
      ) : null}
    </div>
  );
};

export const MessageRow = ({ line }: { line: ChatLine }) => {
  if (line.kind === "tool") {
    return <ToolRow line={line} />;
  }
  return (
    <div className={`${styles.messageRow} ${roleClass(line.kind)}`}>
      <span className={styles.roleLabel}>{roleLabel(line.kind)}</span>
      <span className={styles.messageText}>
        {line.text}
        {line.kind === "assistant" && line.streaming ? "…" : ""}
      </span>
    </div>
  );
};
