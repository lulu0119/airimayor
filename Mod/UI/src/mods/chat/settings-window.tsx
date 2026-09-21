import { useEffect, useMemo, useRef, useState } from "react";
import type { CSSProperties, MouseEvent as ReactMouseEvent, ReactNode } from "react";
import { Panel, Portal } from "cs2/ui";
import chevronDownIcon from "lucide-static/icons/chevron-down.svg";
import eyeIcon from "lucide-static/icons/eye.svg";
import { agentEvents$, fetchModels, getSettings, saveSettings } from "mods/bindings";
import type { ConnectionSettings, ModelsPayload } from "./chat-types";
import { useChatText } from "./locale";
import { setSettingsOpen, setSettingsSaver, useSettingsOpen } from "./panel-visibility";
import {
  clampPanelFrame,
  movePanelFrame,
  resizePanelFrame,
  type PanelFrame,
  type PanelSizeLimits,
  type ResizeEdge,
} from "./panel-frame";
import styles from "./chat.module.scss";

const FRAME_KEY = "cityagent.settings.frame.v4";
const LIMITS: PanelSizeLimits = { minWidth: 320, minHeight: 280 };

const loadFrame = (): PanelFrame | null => {
  try {
    const raw = localStorage.getItem(FRAME_KEY);
    if (raw) {
      const parsed = JSON.parse(raw) as Partial<PanelFrame>;
      if (
        typeof parsed.x === "number" &&
        typeof parsed.y === "number" &&
        typeof parsed.width === "number" &&
        typeof parsed.height === "number"
      ) {
        return clampPanelFrame(
          { x: parsed.x, y: parsed.y, width: parsed.width, height: parsed.height },
          { width: window.innerWidth, height: window.innerHeight },
          LIMITS,
        );
      }
    }
  } catch {
    // Unknown shape or unavailable storage falls back to the default dock.
  }
  return null;
};

const defaultFrame = (): PanelFrame =>
  clampPanelFrame(
    { x: (window.innerWidth - 560) / 2, y: 100, width: 560, height: 540 },
    { width: window.innerWidth, height: window.innerHeight },
    LIMITS,
  );

const RESIZE_HANDLES: { edge: ResizeEdge; className: string }[] = [
  { edge: "n", className: styles.edgeN },
  { edge: "s", className: styles.edgeS },
  { edge: "e", className: styles.edgeE },
  { edge: "w", className: styles.edgeW },
  { edge: "ne", className: styles.edgeNE },
  { edge: "nw", className: styles.edgeNW },
  { edge: "se", className: styles.edgeSE },
  { edge: "sw", className: styles.edgeSW },
];

const parseSettings = (json: string): ConnectionSettings | null => {
  try {
    const parsed = JSON.parse(json) as Partial<ConnectionSettings>;
    if (!parsed || typeof parsed !== "object") {
      return null;
    }
    return {
      endpoint: typeof parsed.endpoint === "string" ? parsed.endpoint : "",
      apiKey: typeof parsed.apiKey === "string" ? parsed.apiKey : "",
      model: typeof parsed.model === "string" ? parsed.model : "",
    };
  } catch {
    return null;
  }
};

// Second-level grouping inside a nav section: every future field lands in
// a group (or a new group), so sections never degrade into a flat pile.
const SettingsGroup = ({ title, children }: { title: string; children: ReactNode }) => (
  <section className={styles.fieldGroup}>
    <div className={styles.groupTitle}>{title}</div>
    {children}
  </section>
);

const parseModels = (json: string): ModelsPayload | null => {
  try {
    const parsed = JSON.parse(json) as Partial<ModelsPayload>;
    if (!parsed || typeof parsed !== "object" || !Array.isArray(parsed.models)) {
      return null;
    }
    return {
      models: parsed.models.filter((id): id is string => typeof id === "string"),
      error: typeof parsed.error === "string" ? parsed.error : "",
    };
  } catch {
    return null;
  }
};

export const SettingsWindow = () => {
  const open = useSettingsOpen();
  const text = useChatText();
  const [frame, setFrame] = useState<PanelFrame | null>(() => loadFrame() ?? defaultFrame());
  const dockRef = useRef<HTMLDivElement>(null);
  const frameRef = useRef<PanelFrame | null>(null);
  frameRef.current = frame;
  const [endpoint, setEndpoint] = useState("");
  const [apiKey, setApiKey] = useState("");
  const [model, setModel] = useState("");
  const [models, setModels] = useState<string[]>([]);
  const [modelsError, setModelsError] = useState("");
  const [fetching, setFetching] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);
  const [showKey, setShowKey] = useState(false);
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (!open) {
      return;
    }
    getSettings();
    const subscription = agentEvents$.subscribe((json) => {
      let parsed: { kind?: string; text?: string } | null = null;
      try {
        parsed = JSON.parse(json) as { kind?: string; text?: string };
      } catch {
        return;
      }
      if (!parsed || typeof parsed.kind !== "string") {
        return;
      }
      if (parsed.kind === "settings") {
        const settings = parseSettings(parsed.text ?? "");
        if (settings) {
          setEndpoint(settings.endpoint);
          setApiKey(settings.apiKey);
          setModel(settings.model);
          setDirty(false);
        }
      } else if (parsed.kind === "models") {
        setFetching(false);
        const payload = parseModels(parsed.text ?? "");
        if (!payload) {
          return;
        }
        setModels(payload.models);
        setModelsError(payload.error);
        // The C# side already defaulted empty/unknown models to the first
        // entry; mirror it here so the combo shows the selection at once.
        if (payload.models.length > 0 && (model.trim() === "" || !payload.models.includes(model))) {
          setModel(payload.models[0]);
          setDirty(true);
        }
      }
    });
    return () => subscription.dispose();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open ]);

  // Model combo candidates: filter while typing; an exact match (usually the
  // selection) shows the full list again for easy switching.
  const candidates = useMemo(() => {
    const query = model.trim().toLowerCase();
    if (!query) {
      return models;
    }
    if (models.some((id) => id.toLowerCase() === query)) {
      return models;
    }
    return models.filter((id) => id.toLowerCase().includes(query));
  }, [model, models]);

  const readParentBounds = (node: HTMLElement | null): { width: number; height: number } => {
    const parent = node?.offsetParent;
    if (!(parent instanceof HTMLElement)) {
      return { width: window.innerWidth, height: window.innerHeight };
    }
    const rect = parent.getBoundingClientRect();
    return { width: rect.width, height: rect.height };
  };

  // Measures the live dock box, exactly like the chat panel does on its
  // first gesture. Called once after the window mounts so later drags and
  // resizes reuse the fixed frame instead of re-measuring mid-gesture.
  const measureDock = (): PanelFrame | null => {
    const node = dockRef.current;
    if (node === null) {
      return null;
    }
    const parent = node.offsetParent;
    const panelRect = node.getBoundingClientRect();
    const bounds =
      parent instanceof HTMLElement
        ? parent.getBoundingClientRect()
        : { left: 0, top: 0, width: window.innerWidth, height: window.innerHeight };
    return clampPanelFrame(
      {
        x: panelRect.left - bounds.left,
        y: panelRect.top - bounds.top,
        width: panelRect.width,
        height: panelRect.height,
      },
      readParentBounds(node),
      LIMITS,
    );
  };

  const captureFrame = (): PanelFrame => {
    if (frameRef.current === null) {
      frameRef.current = measureDock() ?? defaultFrame();
      setFrame(frameRef.current);
    }
    return frameRef.current;
  };

  const beginGesture = (
    edge: ResizeEdge | null,
    event: ReactMouseEvent<HTMLElement>,
  ): void => {
    if (event.button !== 0) {
      return;
    }
    event.preventDefault();
    let current = captureFrame();
    let lastX = event.clientX;
    let lastY = event.clientY;
    const onMove = (moveEvent: MouseEvent): void => {
      const deltaX = moveEvent.clientX - lastX;
      const deltaY = moveEvent.clientY - lastY;
      lastX = moveEvent.clientX;
      lastY = moveEvent.clientY;
      const bounds = readParentBounds(dockRef.current);
      current =
        edge === null
          ? movePanelFrame(current, deltaX, deltaY, bounds, LIMITS)
          : resizePanelFrame(current, edge, deltaX, deltaY, bounds, LIMITS);
      frameRef.current = current;
      setFrame(current);
    };
    const onUp = (): void => {
      window.removeEventListener("mousemove", onMove);
      window.removeEventListener("mouseup", onUp);
      try {
        localStorage.setItem(FRAME_KEY, JSON.stringify(current));
      } catch {
        // Storage is best-effort; the live frame still applies.
      }
    };
    window.addEventListener("mousemove", onMove);
    window.addEventListener("mouseup", onUp);
  };

  const onResizeStart = (edge: ResizeEdge, event: ReactMouseEvent<HTMLElement>): void => {
    event.stopPropagation();
    beginGesture(edge, event);
  };

  // Header drag moves the window. Inputs, buttons, nav, and content keep
  // their own behavior.
  const onDockMouseDown = (event: ReactMouseEvent<HTMLDivElement>): void => {
    const target = event.target as HTMLElement | null;
    if (target?.closest(`.${styles.settingsContent}, .${styles.settingsNav}, button, input`)) {
      return;
    }
    beginGesture(null, event);
  };

  const onSave = (): void => {
    saveSettings(JSON.stringify({ endpoint, apiKey, model }));
    setDirty(false);
    setSettingsOpen(false);
  };

  useEffect(() => {
    if (open) {
      // Re-clamp to the live viewport: the stored frame may predate a
      // resolution change. The geometry is always decided arithmetically,
      // never measured mid-gesture.
      setFrame((current) =>
        current
          ? clampPanelFrame(current, { width: window.innerWidth, height: window.innerHeight }, LIMITS)
          : current,
      );
    }
  }, [open ]);

  useEffect(() => {
    if (open) {
      setSettingsSaver(onSave);
      return () => {
        setSettingsSaver(null);
      };
    }
    setSettingsSaver(null);
    return undefined;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, endpoint, apiKey, model]);

  if (!open) {
    return null;
  }

  const frameStyle: CSSProperties | undefined = frame === null
    ? undefined
    : {
        width: frame.width,
        height: frame.height,
        top: frame.y,
        left: frame.x,
        right: "auto",
        bottom: "auto",
      };

  const onFetch = (): void => {
    setFetching(true);
    setModelsError("");
    fetchModels();
  };

  return (
    <Portal>
      <div
        ref={dockRef}
        className={styles.settingsDock}
        style={frameStyle}
        onMouseDown={onDockMouseDown}
      >
        <Panel
          header={text("Settings.Title", "Settings")}
          className={styles.panelColumn}
          onClose={() => setSettingsOpen(false)}
        >
          <div className={styles.settingsBody}>
            <div className={styles.settingsNav}>
              <button type="button" className={`${styles.navButton} ${styles.navActive}`}>
                {text("Settings.Model.Title", "Model")}
              </button>
            </div>
            <div className={styles.settingsContent}>
              <SettingsGroup title={text("Settings.Connection.Title", "Connection")}>
                  <label className={styles.fieldLabel}>{text("Settings.Connection.Endpoint", "Endpoint")}</label>
                  <input
                    className={styles.fieldInput}
                    value={endpoint}
                    spellCheck={false}
                    placeholder="https://api.openai.com/v1"
                    onChange={(event) => {
                      setEndpoint(event.target.value);
                      setDirty(true);
                    }}
                  />
                  <label className={styles.fieldLabel}>{text("Settings.Connection.ApiKey", "API key")}</label>
                  <div className={styles.keyRow}>
                    <input
                      className={styles.fieldInput}
                      type={showKey ? "text" : "password"}
                      value={apiKey}
                      spellCheck={false}
                      autoComplete="off"
                      placeholder="sk-..."
                      onChange={(event) => {
                        setApiKey(event.target.value);
                        setDirty(true);
                      }}
                    />
                    <button
                      type="button"
                      className={styles.iconButton}
                      title={showKey ? text("Settings.Connection.HideKey", "Hide") : text("Settings.Connection.ShowKey", "Show")}
                      aria-label={showKey ? text("Settings.Connection.HideKey", "Hide") : text("Settings.Connection.ShowKey", "Show")}
                      onClick={() => setShowKey(!showKey)}
                    >
                      <img
                        className={`${styles.fieldIcon}${showKey ? "" : ` ${styles.fieldIconDim}`}`}
                        style={{ maskImage: `url(${eyeIcon})` }}
                        alt=""
                      />
                    </button>
                  </div>
              </SettingsGroup>
              <SettingsGroup title={text("Settings.Model.Title", "Model")}>
                  <label className={styles.fieldLabel}>{text("Settings.Model.Name", "Model")}</label>
                  <div className={styles.modelRow}>
                    <div className={styles.modelCombo}>
                      <input
                        className={styles.fieldInput}
                        value={model}
                        spellCheck={false}
                        autoComplete="off"
                        placeholder={text("Settings.Model.Hint", "Fetch models, pick one, or type a name")}
                        onFocus={() => {
                          if (models.length > 0) {
                            setMenuOpen(true);
                          }
                        }}
                        onChange={(event) => {
                          setModel(event.target.value);
                          setDirty(true);
                          if (models.length > 0) {
                            setMenuOpen(true);
                          }
                        }}
                        onKeyDown={(event) => {
                          if (event.key === "Escape") {
                            setMenuOpen(false);
                          }
                        }}
                        onBlur={() => setMenuOpen(false)}
                      />
                      {models.length > 0 ? (
                        <button
                          type="button"
                          className={styles.comboToggle}
                          onMouseDown={(event) => event.preventDefault()}
                          onClick={() => setMenuOpen(!menuOpen)}
                        >
                          <img
                            className={`${styles.fieldIcon}${menuOpen ? ` ${styles.fieldIconFlip}` : ""}`}
                            style={{ maskImage: `url(${chevronDownIcon})` }}
                            alt=""
                          />
                        </button>
                      ) : null}
                      {menuOpen && candidates.length > 0 ? (
                        <ul className={styles.comboMenu}>
                          {candidates.map((id) => (
                            <li
                              key={id}
                              className={`${styles.comboItem}${id === model ? ` ${styles.comboActive}` : ""}`}
                              onMouseDown={(event) => {
                                event.preventDefault();
                                setModel(id);
                                setDirty(true);
                                setMenuOpen(false);
                              }}
                            >
                              {id}
                            </li>
                          ))}
                        </ul>
                      ) : null}
                    </div>
                    <button type="button" className={styles.smallButton} disabled={fetching} onClick={onFetch}>
                      {fetching ? text("Settings.Model.Fetching", "Fetching…") : text("Settings.Model.Fetch", "Fetch")}
                    </button>
                  </div>
                  {modelsError ? <div className={styles.fieldError}>{modelsError}</div> : null}
                  {!modelsError && models.length > 0 ? (
                    <div className={styles.fieldHint}>
                      {text("Settings.Model.Loaded", "{{count}} models loaded").replace("{{count}}", String(models.length))}
                    </div>
                  ) : null}
              </SettingsGroup>
              <div className={styles.settingsActions}>
                <button type="button" className={styles.smallButton} onClick={onSave}>
                  {text("Settings.Actions.Save", "Save")}
                </button>
              </div>
            </div>
          </div>
        </Panel>
        {RESIZE_HANDLES.map((handle) => (
          <span
            key={handle.edge}
            aria-hidden="true"
            className={`${styles.resizeHandle} ${handle.className}`}
            onMouseDown={(event) => onResizeStart(handle.edge, event)}
          />
        ))}
      </div>
    </Portal>
  );
};
