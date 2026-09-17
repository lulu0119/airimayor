import { Button, Tooltip } from "cs2/ui";
import mayorIcon from "images/mayor.svg";
import { togglePanel, usePanelOpen } from "./panel-visibility";
import { useChatText } from "./locale";
import styles from "./chat.module.scss";

// Persistent entry point on GameTopLeft, RoadBuilder ModIconButton shape:
// floating button, glyph via maskImage so the theme tints it.
export const AgentIconButton = () => {
  const open = usePanelOpen();
  const text = useChatText();
  return (
    <Tooltip tooltip={text("Title", "City Agent")}>
      <Button
        variant="floating"
        className={open ? styles.iconSelected : styles.iconToggle}
        onSelect={togglePanel}
      >
        <img className={styles.iconImage} style={{ maskImage: `url(${mayorIcon})` }} />
      </Button>
    </Tooltip>
  );
};
