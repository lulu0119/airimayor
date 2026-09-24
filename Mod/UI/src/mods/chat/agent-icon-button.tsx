import { Button, Tooltip } from "cs2/ui";
import airi from "images/airi-icon.svg";
import { togglePanel, usePanelOpen } from "./panel-visibility";
import { useChatText } from "./locale";
import styles from "./chat.module.scss";

// Persistent entry point on GameTopLeft. The game button supplies the rounded
// plate. Closed is the darkest pink, the press is the lightest, open is slightly lighter.
export const AgentIconButton = () => {
  const open = usePanelOpen();
  const text = useChatText();
  return (
    <Tooltip tooltip={text("Title", "AIRI Mayor")}>
      <Button
        variant="floating"
        className={open ? styles.iconOpen : styles.iconClosed}
        onSelect={togglePanel}
      >
        <img className={styles.iconImage} src={airi} alt="" />
      </Button>
    </Tooltip>
  );
};
