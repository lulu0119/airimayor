import { useRef, useState } from "react";
import type { ChatLine, ToolRowState } from "./chat-types";
import { Icon } from "@iconify/react";
import altArrowDownLinear from "@iconify-icons/solar/alt-arrow-down-linear";
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

// Gameface img exposes no natural size and ignores object-fit, so CSS alone
// cannot keep the aspect. The agent loop therefore sends the source pixel
// size on the wire; pin an explicit height from the laid-out width. Width is
// rem-scaled by the engine, the wire ratio is unitless, so every image tool
// renders at its source aspect on any resolution.
const ToolImage = ({
  src,
  alt,
  sourceWidth,
  sourceHeight,
}: {
  src: string;
  alt: string;
  sourceWidth: number | null;
  sourceHeight: number | null;
}) => {
  const ref = useRef<HTMLImageElement>(null);
  const fit = () => {
    const img = ref.current;
    if (!img) {
      return;
    }
    const laidWidth = img.clientWidth;
    if (sourceWidth && sourceHeight && sourceWidth > 0 && sourceHeight > 0 && laidWidth > 0) {
      img.style.height = `${Math.round((laidWidth * sourceHeight) / sourceWidth)}px`;
    }
  };
  return (
    <img ref={ref} className={styles.toolImage} src={src} alt={alt} onLoad={fit} />
  );
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
            transform: expanded ? "none" : "rotate(-90deg)",
          }}
        >
          <Icon icon={altArrowDownLinear} />
        </span>
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
          {line.image ? (
            <ToolImage
              src={line.image}
              alt={line.name}
              sourceWidth={line.imageWidth}
              sourceHeight={line.imageHeight}
            />
          ) : null}
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
