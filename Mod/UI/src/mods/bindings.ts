import { bindEvent, bindValue, trigger } from "cs2/api";
import mod from "mod.json";

// Single catalog for every C# <-> UI binding of this mod.
// The C# side lives in Mod/Host/AgentUISystem.cs (group "airimayor").
export const agentState$ = bindValue<string>(mod.id, "state", "{}");
export const agentEvents$ = bindEvent<string>(mod.id, "event");

export const sendChatMessage = (text: string): void => {
  trigger(mod.id, "send", text);
};

export const interruptTurn = (): void => {
  trigger(mod.id, "interrupt");
};

export const pointedPlaces$ = bindValue<string>(mod.id, "places", "[]");
export const pointedMode$ = bindValue<string>(mod.id, "pointMode", "");
export const placeNotice$ = bindValue<string>(mod.id, "placeNotice", "");

export const beginPoint = (mode: string): void => {
  trigger(mod.id, "pointMode", mode);
};

export const removePointedPlace = (id: string): void => {
  trigger(mod.id, "removePlace", id);
};

export const framePointedPlace = (placeJson: string): void => {
  trigger(mod.id, "framePlace", placeJson);
};
