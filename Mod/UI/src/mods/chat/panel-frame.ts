// Floating panel geometry, ported from BeadLoom's floating-panel-frame:
// pure functions over a pixel frame, so move/resize stay testable without a DOM.

export type PanelFrame = {
  x: number;
  y: number;
  width: number;
  height: number;
};

export type PanelBounds = {
  width: number;
  height: number;
};

export type PanelSizeLimits = {
  minWidth: number;
  minHeight: number;
};

export type ResizeEdge = "n" | "s" | "e" | "w" | "ne" | "nw" | "se" | "sw";

export const PANEL_MIN_WIDTH = 280;
export const PANEL_MIN_HEIGHT = 360;

export function clampPanelFrame(
  frame: PanelFrame,
  bounds: PanelBounds,
  limits: PanelSizeLimits,
): PanelFrame {
  const width = Math.min(Math.max(frame.width, limits.minWidth), Math.max(limits.minWidth, bounds.width));
  const height = Math.min(Math.max(frame.height, limits.minHeight), Math.max(limits.minHeight, bounds.height));
  const maxX = Math.max(0, bounds.width - width);
  const maxY = Math.max(0, bounds.height - height);
  return {
    x: Math.min(Math.max(0, frame.x), maxX),
    y: Math.min(Math.max(0, frame.y), maxY),
    width,
    height,
  };
}

export function movePanelFrame(
  frame: PanelFrame,
  deltaX: number,
  deltaY: number,
  bounds: PanelBounds,
  limits: PanelSizeLimits,
): PanelFrame {
  return clampPanelFrame({ ...frame, x: frame.x + deltaX, y: frame.y + deltaY }, bounds, limits);
}

export function resizePanelFrame(
  frame: PanelFrame,
  edge: ResizeEdge,
  deltaX: number,
  deltaY: number,
  bounds: PanelBounds,
  limits: PanelSizeLimits,
): PanelFrame {
  const west = edge.includes("w");
  const east = edge.includes("e");
  const north = edge.includes("n");
  const south = edge.includes("s");

  let left = frame.x + (west ? deltaX : 0);
  let right = frame.x + frame.width + (east ? deltaX : 0);
  let top = frame.y + (north ? deltaY : 0);
  let bottom = frame.y + frame.height + (south ? deltaY : 0);

  left = Math.max(0, left);
  right = Math.min(bounds.width, right);
  top = Math.max(0, top);
  bottom = Math.min(bounds.height, bottom);

  if (right - left < limits.minWidth) {
    if (west && !east) {
      left = Math.max(0, right - limits.minWidth);
      right = Math.min(bounds.width, left + limits.minWidth);
    } else {
      right = Math.min(bounds.width, left + limits.minWidth);
      left = Math.max(0, right - limits.minWidth);
    }
  }

  if (bottom - top < limits.minHeight) {
    if (north && !south) {
      top = Math.max(0, bottom - limits.minHeight);
      bottom = Math.min(bounds.height, top + limits.minHeight);
    } else {
      bottom = Math.min(bounds.height, top + limits.minHeight);
      top = Math.max(0, bottom - limits.minHeight);
    }
  }

  return {
    x: left,
    y: top,
    width: right - left,
    height: bottom - top,
  };
}
