import { useLocalization } from "cs2/l10n";

// Key namespace matches Mod/ChatLocale.Id: CitiesSkylines2Agent.Chat.<name>.
// The C# side registers one dictionary source per game language; the game
// picks the active one, so the UI never detects the language itself.
export const chatTextId = (name: string): string => `CitiesSkylines2Agent.Chat.${name}`;

export const useChatText = (): ((name: string, fallback: string) => string) => {
  const { translate } = useLocalization();
  return (name, fallback) => translate(chatTextId(name), fallback) ?? fallback;
};
