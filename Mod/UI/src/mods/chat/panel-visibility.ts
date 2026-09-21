// Panel visibility shared across mount roots: the docked chat panel hangs
// on GameBottomRight while its icon button hangs on GameTopLeft, so React
// state cannot travel through props. One module-level store + subscription.

import { useSyncExternalStore } from "react";

interface ToggleStore {
  get: () => boolean;
  set: (next: boolean) => void;
  toggle: () => void;
  subscribe: (listener: () => void) => () => void;
}

const createToggleStore = (initial: boolean): ToggleStore => {
  let open = initial;
  const listeners = new Set<() => void>();
  return {
    get: () => open,
    set: (next: boolean) => {
      if (open !== next) {
        open = next;
        listeners.forEach((listener) => listener());
      }
    },
    toggle: () => {
      open = !open;
      listeners.forEach((listener) => listener());
    },
    subscribe: (listener: () => void) => {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
  };
};

const panelStore = createToggleStore(true);
const settingsStore = createToggleStore(false);

export const setPanelOpen = (next: boolean): void => panelStore.set(next);
export const togglePanel = (): void => panelStore.toggle();
export const usePanelOpen = (): boolean =>
  useSyncExternalStore(panelStore.subscribe, panelStore.get);

export const setSettingsOpen = (next: boolean): void => settingsStore.set(next);
export const toggleSettings = (): void => settingsStore.toggle();
export const useSettingsOpen = (): boolean =>
  useSyncExternalStore(settingsStore.subscribe, settingsStore.get);

// The chat header gear toggles the window: opening it fresh, or saving and
// closing it when it is already open. The window registers its saver while
// mounted so the header never reaches into the form state.
let settingsSaver: (() => void) | null = null;

export const setSettingsSaver = (saver: (() => void) | null): void => {
  settingsSaver = saver;
};

export const smartToggleSettings = (): void => {
  if (!settingsStore.get()) {
    settingsStore.set(true);
    return;
  }
  if (settingsSaver) {
    settingsSaver();
  } else {
    settingsStore.set(false);
  }
};
