// Message list with stick-to-bottom scrolling: new lines pull the view down
// only while the player is already near the bottom, so reading history up
// above is never yanked away.

import { useEffect, useRef } from "react";
import { Scrollable } from "cs2/ui";
import type { ChatLine } from "./chat-types";
import { useChatText } from "./locale";
import { MessageRow } from "./message-row";
import styles from "./chat.module.scss";

const nearBottomMargin = 120;

interface MessageListProps {
  lines: ChatLine[];
  busy: boolean;
}

export const MessageList = ({ lines, busy }: MessageListProps) => {
  const text = useChatText();
  // Scrollable forwards its ref to the scroll container; the game no longer
  // exports a ScrollController class to reach it through.
  const listRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    const view = listRef.current;
    if (!view) {
      return;
    }
    const distance = view.scrollHeight - view.scrollTop - view.clientHeight;
    if (distance < nearBottomMargin) {
      view.scrollTop = view.scrollHeight;
    }
  }, [lines]);

  const last = lines[lines.length - 1];
  const showThinking =
    busy &&
    (lines.length === 0 ||
      last.kind === "user" ||
      (last.kind === "assistant" && !last.streaming && last.text.trim().length === 0));

  return (
    <Scrollable vertical trackVisibility="scrollable" className={styles.messageList} ref={listRef}>
      {lines.length === 0 && !busy ? (
        <div className={styles.emptyHint}>
          {text("Empty", "Ask the mayor to build, zone, or fix city services.")}
        </div>
      ) : null}
      {lines.map((line) => (
        <MessageRow key={line.id} line={line} />
      ))}
      {showThinking ? (
        <div className={styles.thinkingRow} role="status">
          <span>{text("Thinking", "Thinking")}</span>
          <span className={styles.thinkingDots}>
            <span>.</span>
            <span>.</span>
            <span>.</span>
          </span>
        </div>
      ) : null}
    </Scrollable>
  );
};
