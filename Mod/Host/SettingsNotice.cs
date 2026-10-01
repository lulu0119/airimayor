using System;
using Game.SceneFlow;
using Game.UI;

namespace airimayor.Host
{
    /// <summary>
    /// One line the player can read from the main menu. Settings tests use
    /// this instead of applying settings.
    /// </summary>
    internal static class SettingsNotice
    {
        public static void Show(string message)
        {
            string text = string.IsNullOrWhiteSpace(message) ? "No reply." : message.Trim();
            try
            {
                GameManager manager = GameManager.instance;
                if (manager != null && manager.userInterface != null && manager.userInterface.appBindings != null)
                {
                    manager.userInterface.appBindings.ShowMessageDialog(
                        new MessageDialog("AIRI Mayor", text, "OK"),
                        (Action<int>)(_ => { }));
                    return;
                }
            }
            catch (Exception e)
            {
                AgentTimeline.Warn("settings", e.GetType().Name + ": " + e.Message);
            }
            AgentTimeline.Info("settings", text);
        }
    }
}
