import { useLayoutEffect, useRef, useState } from "react";
import type { ChatLine, ToolRowState } from "./chat-types";
import { Icon } from "@iconify/react";
import altArrowDownBoldDuotone from "@iconify-icons/solar/alt-arrow-down-bold-duotone";
import { useChatText } from "./locale";
import { jpegPixelSize } from "./jpeg-size";
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

// Gameface percentage height follows the content box. A padding ratio box
// leaves that box at 0, so the image lays out 0px tall. Pin a pixel height
// from the frame width and the JPEG ratio.
const ToolImage = ({ src, alt }: { src: string; alt: string }) => {
  const frameRef = useRef<HTMLDivElement>(null);
  const imgRef = useRef<HTMLImageElement>(null);
  const size = jpegPixelSize(src);
  useLayoutEffect(() => {
    const frame = frameRef.current;
    const img = imgRef.current;
    if (!frame || !img) {
      return;
    }
    const apply = () => {
      const width = frame.clientWidth;
      const appliedHeight = size && width > 0 ? Math.round((width * size.height) / size.width) : 0;
      if (appliedHeight > 0 && img.style.height !== `${appliedHeight}px`) {
        img.style.height = `${appliedHeight}px`;
      }
    };
    apply();
    let observer: ResizeObserver | null = null;
    if (typeof ResizeObserver === "function") {
      observer = new ResizeObserver(apply);
      observer.observe(frame);
    }
    return () => observer?.disconnect();
  }, [src, size?.width, size?.height]);
  return (
    <div ref={frameRef} className={styles.toolImageFrame}>
      <img ref={imgRef} className={styles.toolImage} src={src} alt={alt} />
    </div>
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
          <Icon icon={altArrowDownBoldDuotone} />
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
            <ToolImage src={line.image} alt={line.name} />
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
        : text("Role.Mayor", "AIRI");
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
