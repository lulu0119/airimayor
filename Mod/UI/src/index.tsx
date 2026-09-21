import { ModRegistrar } from "cs2/modding";
import { AgentIconButton } from "mods/chat/agent-icon-button";
import { ChatPanel } from "mods/chat/chat-panel";
import { SettingsWindow } from "mods/chat/settings-window";

const register: ModRegistrar = (moduleRegistry) => {
  // Persistent entry point, RoadBuilder ModIconButton shape.
  moduleRegistry.append("GameTopLeft", AgentIconButton);
  // In-city only. Portal escapes append-parent layout (never bare "Game").
  moduleRegistry.append("GameBottomRight", ChatPanel);
  moduleRegistry.append("GameBottomRight", SettingsWindow);
};

export default register;
