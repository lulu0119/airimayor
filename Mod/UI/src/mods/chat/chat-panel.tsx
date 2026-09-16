import { useState } from "react";
import type { ReactNode } from "react";
import { Button, FOCUS_AUTO, Panel, Portal } from "cs2/ui";
import { useChat } from "./use-chat";
import { MessageList } from "./message-list";
import { StatusBar } from "./status-bar";
import { Composer } from "./composer";
import styles from "./chat.module.scss";

export const ChatPanel = ({ children }: { children?: ReactNode }) => {
  const chat = useChat();
  const [open, setOpen] = useState(true);

  return (
    <>
      {children}
      <Portal>
        <div className={styles.dock}>
          {open ? (
            <Panel
              header="City Agent"
              focusKey={FOCUS_AUTO}
              className={styles.panelColumn}
              onClose={() => setOpen(false)}
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
          ) : (
            <div className={styles.openButtonWrap}>
              <Button variant="primary" onSelect={() => setOpen(true)}>
                City Agent
              </Button>
            </div>
          )}
        </div>
      </Portal>
    </>
  );
};
