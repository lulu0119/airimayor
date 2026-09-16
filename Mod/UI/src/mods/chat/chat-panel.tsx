import type { ReactNode } from "react";
import { FOCUS_AUTO, Panel, Portal } from "cs2/ui";
import { useChat } from "./use-chat";
import { MessageList } from "./message-list";
import { StatusBar } from "./status-bar";
import { Composer } from "./composer";
import { setPanelOpen, usePanelOpen } from "./panel-visibility";
import styles from "./chat.module.scss";

export const ChatPanel = ({ children }: { children?: ReactNode }) => {
  const chat = useChat();
  const open = usePanelOpen();

  if (!open) {
    return <>{children}</>;
  }

  return (
    <>
      {children}
      <Portal>
        <div className={styles.dock}>
          <Panel
            header="City Agent"
            focusKey={FOCUS_AUTO}
            className={styles.panelColumn}
            onClose={() => setPanelOpen(false)}
                footer={
                  <Composer
                    sessionReady={chat.session !== ""}
                    busy={chat.busy}
                    onSend={(text) => chat.send(text)}
                    onInterrupt={() => chat.interrupt()}
                  />
                }
              >
                <StatusBar
                  status={chat.status}
                  busy={chat.busy}
                  pending={chat.pending}
                  note={chat.note}
                  context={chat.context}
                />
                <MessageList lines={chat.lines} busy={chat.busy} />
          </Panel>
        </div>
      </Portal>
    </>
  );
};
