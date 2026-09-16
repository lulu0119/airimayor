import { useState } from "react";
import type { KeyboardEvent } from "react";
import sendIcon from "images/send.svg";
import stopIcon from "images/stop.svg";
import styles from "./chat.module.scss";

interface ComposerProps {
  sessionReady: boolean;
  busy: boolean;
  onSend: (text: string) => void;
  onInterrupt: () => void;
}

export const Composer = ({ sessionReady, busy, onSend, onInterrupt }: ComposerProps) => {
  const [draft, setDraft] = useState("");

  const submit = () => {
    const text = draft.trim();
    if (text.length === 0 || !sessionReady) {
      return;
    }
    onSend(text);
    setDraft("");
  };

  const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      submit();
    }
  };

  return (
    <div className={styles.composer}>
      <textarea
        className={styles.composerInput}
        rows={3}
        value={draft}
        disabled={!sessionReady}
        placeholder={
          !sessionReady ? "Loading city…" : busy ? "Type to queue…" : "Message the mayor…"
        }
        onChange={(event) => setDraft(event.target.value)}
        onKeyDown={onKeyDown}
      />
      <div className={styles.composerActions}>
        {busy ? (
          <button
            type="button"
            title="Stop"
            aria-label="Stop"
            className={`${styles.actionButton} ${styles.stopButton}`}
            onClick={onInterrupt}
          >
            <img
              className={styles.actionIcon}
              style={{ maskImage: `url(${stopIcon})` }}
              alt=""
            />
          </button>
        ) : (
          <button
            type="button"
            title="Send"
            aria-label="Send"
            className={`${styles.actionButton} ${styles.sendButton}`}
            onClick={submit}
            disabled={!sessionReady || draft.trim().length === 0}
          >
            <img
              className={styles.actionIcon}
              style={{ maskImage: `url(${sendIcon})` }}
              alt=""
            />
          </button>
        )}
      </div>
    </div>
  );
};
