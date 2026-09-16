// Panel visibility shared across mount roots: the docked chat panel hangs
// on GameBottomRight while its icon button hangs on GameTopLeft, so React
// state cannot travel through props. One module-level store + subscription.

import { useSyncExternalStore } from "react";

let open = true;
const listeners = new Set<() => void>();

const emit = (): void => {
  listeners.forEach((listener) => listener());
};

export const setPanelOpen = (next: boolean): void => {
  if (open !== next) {
    open = next;
    emit();
  }
};

export const togglePanel = (): void => {
  setPanelOpen(!open);
};

const subscribe = (listener: () => void): (() => void) => {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
};

const getSnapshot = (): boolean => open;

export const usePanelOpen = (): boolean => useSyncExternalStore(subscribe, getSnapshot);
