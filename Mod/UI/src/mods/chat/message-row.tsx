import { useState } from "react";
import type { ChatLine, ToolRowState } from "./chat-types";
import chevronDownIcon from "images/chevron-down.svg";
import { useChatText } from "./locale";
import styles from "./chat.module.scss";

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

const ToolRow = ({ line }: { line: Extract<ChatLine, { kind: "tool" }> }) => {
  const [expanded, setExpanded] = useState(false);
  const text = useChatText();
  const stateLabel: Record<ToolRowState, string> = {
    running: text("Tool.Running", "Running"),
    done: text("Tool.Done", "Done"),
    error: text("Tool.Error", "Error"),
    interrupted: text("Tool.Interrupted", "Interrupted"),
  };
  return (
    <div className={styles.toolRow}>
      <div
        className={styles.toolHeader}
        onClick={() => setExpanded(!expanded)}
      >
        <span className={`${styles.toolDot} ${toolDotClass(line.state)}`} />
        <span className={styles.toolName}>{line.name}</span>
        <span className={styles.toolState}>{stateLabel[line.state]}</span>
        <span
          className={styles.toolChevron}
          style={{
            maskImage: `url(${chevronDownIcon})`,
            transform: expanded ? "none" : "rotate(-90deg)",
          }}
        />
      </div>
      {expanded ? (
        <div className={styles.toolBody}>
          {line.args ? (
            <>
              <div className={styles.toolSectionLabel}>{text("Tool.Arguments", "Arguments")}</div>
              <pre className={styles.toolPre}>{line.args}</pre>
            </>
          ) : null}
          <div className={styles.toolSectionLabel}>{text("Tool.Result", "Result")}</div>
          <pre className={styles.toolPre}>{line.result ?? text("Tool.NoResult", "No result yet.")}</pre>
        </div>
      ) : null}
    </div>
  );
};

export const MessageRow = ({ line }: { line: ChatLine }) => {
  const text = useChatText();
  if (line.kind === "tool") {
    return <ToolRow line={line} />;
  }
  const roleName =
    line.kind === "user"
      ? text("Role.You", "You")
      : line.kind === "error"
        ? text("Role.Error", "Error")
        : text("Role.Mayor", "Mayor");
  return (
    <div className={`${styles.messageRow} ${roleClass(line.kind)}`}>
      <span className={styles.roleLabel}>{roleName}</span>
      <span className={styles.messageText}>
        {line.text}
        {line.kind === "assistant" && line.streaming ? "…" : ""}
      </span>
    </div>
  );
};
