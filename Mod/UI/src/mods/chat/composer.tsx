import { useState } from "react";
import type { KeyboardEvent } from "react";
import { useValue } from "cs2/api";
import { Icon } from "@iconify/react";
import mapPointBoldDuotone from "@iconify-icons/solar/map-point-bold-duotone";
import plainBoldDuotone from "@iconify-icons/solar/plain-bold-duotone";
import stopCircleBoldDuotone from "@iconify-icons/solar/stop-circle-bold-duotone";
import {
  beginPoint,
  framePointedPlace,
  placeNotice$,
  pointedMode$,
  pointedPlaces$,
  removePointedPlace,
} from "mods/bindings";
import { useChatText } from "./locale";
import { PlaceCards } from "./place-cards";
import { readPlaces } from "./place";
import type { PointedPlace } from "./place";
import styles from "./chat.module.scss";

interface ComposerProps {
  loading: boolean;
  busy: boolean;
  onSend: (text: string) => void;
  onInterrupt: () => void;
}

const noticeCopy = (
  code: string,
  text: (name: string, fallback: string) => string,
): string => {
  switch (code) {
    case "full":
      return text("Place.Full", "You can point at 8 places at a time.");
    case "missing-building":
      return text("Place.MissingBuilding", "That building is gone.");
    case "missing-road":
      return text("Place.MissingRoad", "That road is gone.");
    case "missing-line":
      return text("Place.MissingLine", "That line is gone.");
    case "busy":
      return text("Place.Busy", "The city is busy building; point again in a moment.");
    default:
      return "";
  }
};

export const Composer = ({ loading, busy, onSend, onInterrupt }: ComposerProps) => {
  const [draft, setDraft] = useState("");
  const text = useChatText();
  const places = readPlaces(useValue(pointedPlaces$));
  const mode = useValue(pointedMode$);
  const notice = noticeCopy(useValue(placeNotice$), text);
  const canSend = draft.trim().length > 0 || places.length > 0;

  const submit = () => {
    const trimmed = draft.trim();
    if (!canSend || loading) {
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

  const frame = (place: PointedPlace) => {
    framePointedPlace(JSON.stringify(place));
  };

  const placeholder = loading
    ? text("Composer.Loading", "Loading city…")
    : text("Composer.Ready", "Message AIRI…");
  const sendLabel = text("Composer.Send", "Send");
  const stopLabel = text("Composer.Stop", "Stop");
  const selectLabel = text("Composer.SelectPlace", "Select place or building");
  const pickHint = text(
    "Composer.PickHint",
    "Click a building, road, or the ground in the city.",
  );
  const armed = mode === "place";

  return (
    <div className={styles.composer}>
      {notice ? <div className={styles.placeNotice}>{notice}</div> : null}
      {!notice && armed ? <div className={styles.pickHint}>{pickHint}</div> : null}
      <PlaceCards places={places} onFrame={frame} onRemove={removePointedPlace} />
      <textarea
        className={styles.composerInput}
        rows={3}
        value={draft}
        disabled={loading}
        placeholder={placeholder}
        onChange={(event) => setDraft(event.target.value)}
        onKeyDown={onKeyDown}
      />
      <div className={styles.placeButtonWrap}>
        <button
          type="button"
          title={selectLabel}
          aria-label={selectLabel}
          className={`${styles.actionButton} ${armed ? styles.placeButtonOn : ""}`}
          disabled={loading}
          onClick={() => beginPoint("place")}
        >
          <span className={styles.actionIcon}>
            <Icon icon={mapPointBoldDuotone} />
          </span>
        </button>
      </div>
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
              <Icon icon={stopCircleBoldDuotone} />
            </span>
          </button>
        ) : null}
        <button
          type="button"
          title={sendLabel}
          aria-label={sendLabel}
          className={`${styles.actionButton} ${styles.sendButton}`}
          onClick={submit}
          disabled={loading || !canSend}
        >
          <span className={styles.actionIcon}>
            <Icon icon={plainBoldDuotone} />
          </span>
        </button>
      </div>
    </div>
  );
};
