import { useState } from "react";
import type { KeyboardEvent } from "react";
import { Icon } from "@iconify/react";
import plainLinear from "@iconify-icons/solar/plain-linear";
import stopCircleLinear from "@iconify-icons/solar/stop-circle-linear";
import { useChatText } from "./locale";
import styles from "./chat.module.scss";

interface ComposerProps {
  loading: boolean;
  busy: boolean;
  onSend: (text: string) => void;
  onInterrupt: () => void;
}

export const Composer = ({ loading, busy, onSend, onInterrupt }: ComposerProps) => {
  const [draft, setDraft] = useState("");
  const text = useChatText();

  const submit = () => {
    const trimmed = draft.trim();
    if (trimmed.length === 0 || loading) {
      return;
    }
    onSend(trimmed);
    setDraft("");
  };

  const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      submit();
    }
  };

  const placeholder = loading
    ? text("Composer.Loading", "Loading city…")
    : text("Composer.Ready", "Message AIRI…");
  const sendLabel = text("Composer.Send", "Send");
  const stopLabel = text("Composer.Stop", "Stop");

  return (
    <div className={styles.composer}>
      <textarea
        className={styles.composerInput}
        rows={3}
        value={draft}
        disabled={loading}
        placeholder={placeholder}
        onChange={(event) => setDraft(event.target.value)}
        onKeyDown={onKeyDown}
      />
      <div className={styles.composerActions}>
        {busy ? (
          <button
            type="button"
            title={stopLabel}
            aria-label={stopLabel}
            className={`${styles.actionButton} ${styles.stopButton}`}
            onClick={onInterrupt}
          >
            <span className={styles.actionIcon}>
              <Icon icon={stopCircleLinear} />
            </span>
          </button>
        ) : null}
        <button
          type="button"
          title={sendLabel}
          aria-label={sendLabel}
          className={`${styles.actionButton} ${styles.sendButton}`}
          onClick={submit}
          disabled={loading || draft.trim().length === 0}
        >
          <span className={styles.actionIcon}>
            <Icon icon={plainLinear} />
          </span>
        </button>
      </div>
    </div>
  );
};
