import { useState } from "react";
import type { KeyboardEvent } from "react";
import { Button } from "cs2/ui";
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
        rows={2}
        value={draft}
        disabled={!sessionReady}
        placeholder={
          !sessionReady ? "Loading city…" : busy ? "Type to queue…" : "Message the mayor…"
        }
        onChange={(event) => setDraft(event.target.value)}
        onKeyDown={onKeyDown}
      />
      <div className={styles.composerRow}>
        <span className={styles.composerHint}>Enter to send, Shift+Enter for a new line</span>
        {busy ? (
          <Button variant="default" onSelect={onInterrupt}>
            Stop
          </Button>
        ) : (
          <Button
            variant="primary"
            onSelect={submit}
          >
            Send
          </Button>
        )}
      </div>
    </div>
  );
};
