export interface RunningTool {
  name: string;
  args: string;
}

export type ComposerPhase = "loading" | "ready" | "interrupt" | "finish" | "endWait";

export const isTimeAdvance = (tool: RunningTool | null): boolean => {
  if (!tool || tool.name !== "set_simulation") {
    return false;
  }
  try {
    const parsed = JSON.parse(tool.args) as { action?: unknown };
    return parsed.action === "advance";
  } catch {
    return false;
  }
};

export const phaseCopy = (
  phase: ComposerPhase,
): { id: string; fallback: string } => {
  switch (phase) {
    case "loading":
      return { id: "Composer.Loading", fallback: "Loading city…" };
    case "interrupt":
      return { id: "Composer.InterruptReply", fallback: "Sending interrupts this reply" };
    case "finish":
      return { id: "Composer.FinishStep", fallback: "Sending waits until this step's tools finish" };
    case "endWait":
      return { id: "Composer.EndWait", fallback: "Sending ends this wait" };
    case "ready":
      return { id: "Composer.Ready", fallback: "Message the mayor…" };
  }
};

export const composerPhase = (
  sessionReady: boolean,
  status: string,
  tool: RunningTool | null,
): ComposerPhase => {
  if (!sessionReady) {
    return "loading";
  }
  if (status === "Thinking") {
    return "interrupt";
  }
  if (status === "Working") {
    return isTimeAdvance(tool) ? "endWait" : "finish";
  }
  return "ready";
};
