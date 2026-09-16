import { bindEvent, bindValue, trigger } from "cs2/api";
import mod from "mod.json";

// Single catalog for every C# <-> UI binding of this mod.
// The C# side lives in Mod/Agent/AgentUISystem.cs (group "CitiesSkylines2Agent").
export const agentState$ = bindValue<string>(mod.id, "state", "{}");
export const agentEvents$ = bindEvent<string>(mod.id, "event");

export const sendChatMessage = (text: string): void => {
  trigger(mod.id, "send", text);
};

export const interruptTurn = (): void => {
  trigger(mod.id, "interrupt");
};
