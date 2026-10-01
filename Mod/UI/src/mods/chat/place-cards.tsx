import type { PointedPlace } from "./place";
import { useChatText } from "./locale";
import styles from "./chat.module.scss";

interface PlaceCardsProps {
  places: PointedPlace[];
  onFrame: (place: PointedPlace) => void;
  onRemove?: (id: string) => void;
}

export const PlaceCards = ({ places, onFrame, onRemove }: PlaceCardsProps) => {
  const text = useChatText();
  if (places.length === 0) {
    return null;
  }
  const removeLabel = text("Place.Remove", "Remove");
  return (
    <div className={styles.placeCards}>
      {places.map((place, index) => (
        <span key={place.id || `${place.kind}-${index}`} className={styles.clipInline}>
          <span role="button" onClick={() => onFrame(place)}>
            {place.name}
          </span>
          {onRemove ? (
            <span
              role="button"
              className={styles.placeChipRemove}
              aria-label={removeLabel}
              title={removeLabel}
              onClick={() => onRemove(place.id)}
            >
              ×
            </span>
          ) : null}
        </span>
      ))}
    </div>
  );
};
