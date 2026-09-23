using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CitiesSkylines2Agent.Agent;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.UI.Widgets;

namespace CitiesSkylines2Agent
{
    public enum VisionToolMode
    {
        Off,
        On,
    }

    public enum ApiKind
    {
        ChatCompletions,
        Responses,
    }

    [FileLocation(nameof(CitiesSkylines2Agent))]
    [SettingsUIGroupOrder(kConnectionGroup, kAgentGroup)]
    [SettingsUIShowGroupName(kConnectionGroup, kAgentGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kConnectionGroup = "Connection";
        public const string kAgentGroup = "Agent";

        public static Setting Instance { get; set; }

        public Setting(IMod mod) : base(mod) { }

        // ---- Connection -----------------------------------

        [SettingsUISection(kSection, kConnectionGroup)]
        [SettingsUITextInput]
        public string Endpoint { get; set; } = "https://api.openai.com/v1";

        [SettingsUISection(kSection, kConnectionGroup)]
        [SettingsUITextInput]
        public string ApiKey { get; set; } = "";

        [SettingsUISection(kSection, kConnectionGroup)]
        [SettingsUIDropdown(typeof(Setting), nameof(GetModelPresetItems))]
        [SettingsUIValueVersion(typeof(Setting), nameof(ModelPresetVersion))]
        public string ModelPreset
        {
            get => Model;
            set { if (!string.IsNullOrEmpty(value)) { Model = value; } }
        }

        [SettingsUISection(kSection, kConnectionGroup)]
        [SettingsUIButton]
        public bool FetchModels
        {
            set { if (value) { StartFetchModels(); } }
        }

        [SettingsUISection(kSection, kConnectionGroup)]
        [SettingsUITextInput]
        public string Model { get; set; } = "";

        [SettingsUISection(kSection, kConnectionGroup)]
        public ApiKind Api { get; set; } = ApiKind.ChatCompletions;

        // ---- Agent ---------------------------------------

        [SettingsUISection(kSection, kAgentGroup)]
        public bool AutoStart { get; set; } = true;

        [SettingsUISection(kSection, kAgentGroup)]
        public bool Continuous { get; set; } = true;

        [SettingsUISection(kSection, kAgentGroup)]
        public bool AllowProgressionPurchases { get; set; } = true;

        [SettingsUISection(kSection, kAgentGroup)]
        public bool AllowDemolition { get; set; } = true;

        [SettingsUISection(kSection, kAgentGroup)]
        public bool EnableDevelopmentTools { get; set; } = false;

        [SettingsUISection(kSection, kAgentGroup)]
        public VisionToolMode VisionTools { get; set; } = VisionToolMode.Off;

        [SettingsUISection(kSection, kAgentGroup)]
        [SettingsUISlider(min = 16_000, max = 2_000_000, step = 1_000)]
        public int WindowTokens { get; set; } = 200_000;

        // ---- Static facade ---------------------------------

        public static string StaticEndpoint => Instance?.Endpoint ?? "https://api.openai.com/v1";
        public static string StaticModel => Instance?.Model ?? "";

        private const string StartupPrompt = "Run the city continuously: fix growth-blocking problems first, then expand with demand; act, don't just report.";

        public static string StaticStartupPrompt => StartupPrompt;

        public static bool StaticAutoStart => Instance?.AutoStart ?? true;
        public static bool StaticContinuous => Instance?.Continuous ?? true;
        public static bool StaticAllowProgressionPurchases =>
            Instance?.AllowProgressionPurchases ?? true;
        public static bool StaticAllowDemolition => Instance?.AllowDemolition ?? true;
        public static bool StaticEnableDevelopmentTools =>
            Instance?.EnableDevelopmentTools ?? false;
        public static VisionToolMode StaticVisionToolMode =>
            Instance?.VisionTools ?? VisionToolMode.Off;
        public static ApiKind StaticApiKind =>
            Instance?.Api ?? ApiKind.ChatCompletions;
        public static string StaticApiKey => Instance?.ApiKey ?? "";
        public static long StaticWindowTokens => Instance?.WindowTokens ?? 200_000;

        // ---- Fetched model presets (Options dropdown) --------

        private static readonly object s_ModelPresetLock = new object();
        private static List<string> s_ModelPresets = new List<string>();

        public int ModelPresetVersion { get; set; }

        public static DropdownItem<string>[] GetModelPresetItems()
        {
            lock (s_ModelPresetLock)
            {
                IEnumerable<string> items = s_ModelPresets;
                string current = Instance?.Model;
                if (!items.Any() && !string.IsNullOrEmpty(current))
                {
                    items = new[] { current };
                }
                else if (!string.IsNullOrEmpty(current) && !items.Contains(current))
                {
                    items = new[] { current }.Concat(items);
                }
                return items
                    .Select(id => new DropdownItem<string> { value = id, displayName = id })
                    .ToArray();
            }
        }

        private void StartFetchModels()
        {
            string endpoint = Endpoint;
            string apiKey = ApiKey;
            Task.Run(async () =>
            {
                ModelCatalog.FetchResult result = await ModelCatalog.FetchAsync(endpoint, apiKey);
                if (result.Models.Count == 0)
                {
                    CS2MCP.Mod.Log.Info("fetch-models: " + result.Error);
                    return;
                }
                lock (s_ModelPresetLock)
                {
                    s_ModelPresets = result.Models;
                }
                Setting instance = Instance;
                if (instance != null)
                {
                    if (string.IsNullOrEmpty(instance.Model) || !result.Models.Contains(instance.Model))
                    {
                        instance.Model = result.Models[0];
                    }
                    instance.ModelPresetVersion++;
                    instance.ApplyAndSave();
                }
                CS2MCP.Mod.Log.Info($"fetch-models: loaded {result.Models.Count} models.");
            });
        }

        public override void SetDefaults()
        {
            Endpoint = "https://api.openai.com/v1";
            ApiKey = "";
            Model = "";
            AutoStart = true;
            Continuous = true;
            AllowProgressionPurchases = true;
            AllowDemolition = true;
            EnableDevelopmentTools = false;
            VisionTools = VisionToolMode.Off;
            Api = ApiKind.ChatCompletions;
            WindowTokens = 200_000;
        }
    }

    public static class ChatLocale
    {
        public static string Id(string name) => $"CitiesSkylines2Agent.Chat.{name}";
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;

        public LocaleEN(Setting setting) { m_Setting = setting; }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "AIRI Mayor" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kConnectionGroup), "Connection" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kAgentGroup), "Agent" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Endpoint)), "Endpoint" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Endpoint)), "OpenAI-compatible API base URL." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ApiKey)), "API key" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ApiKey)), "Stored in settings only." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Model)), "Model" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Model)), "e.g. gpt-5.6-sol, deepseek-v4-flash." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.FetchModels)), "Fetch models" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.FetchModels)), "Load the model list from the endpoint; selects the first model when the current one is empty or missing." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ModelPreset)), "Model preset" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ModelPreset)), "Pick a fetched model; writes into Model. Manual input stays in Model." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.AutoStart)), "Auto-start" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.AutoStart)), "Start a turn on city load." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Continuous)), "Continue" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Continuous)), "Keep the agent running without stopping." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.AllowProgressionPurchases)), "Allow development purchases" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.AllowProgressionPurchases)), "Let the agent spend earned Development Points on the Development Tree." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.AllowDemolition)), "Allow demolition" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.AllowDemolition)), "Let the agent bulldoze buildings and road segments without a confirmation dialog." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableDevelopmentTools)), "Development / acceptance tools" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableDevelopmentTools)), "Expose diagnostic, experimental, and manual-save tools to the in-game agent." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.VisionTools)), "Visual tools" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.VisionTools)), "Auto follows model-name capabilities; On and Off force the result." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Api)), "API kind" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Api)), "Chat Completions or Responses. This is the loop's request shape, not the server model limit." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.WindowTokens)), "Window tokens" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.WindowTokens)), "How many tokens the loop treats as the window. This does not change the server model limit." },

                { m_Setting.GetEnumValueLocaleID(VisionToolMode.Off), "Off" },
                { m_Setting.GetEnumValueLocaleID(VisionToolMode.On), "On" },
                { m_Setting.GetEnumValueLocaleID(ApiKind.ChatCompletions), "Chat Completions" },
                { m_Setting.GetEnumValueLocaleID(ApiKind.Responses), "Responses" },

                { ChatLocale.Id("Title"), "AIRI Mayor" },
                { ChatLocale.Id("Composer.Loading"), "Loading city…" },
                { ChatLocale.Id("Composer.Ready"), "Message AIRI…" },
                { ChatLocale.Id("Composer.Send"), "Send" },
                { ChatLocale.Id("Composer.Stop"), "Stop" },
                { ChatLocale.Id("Empty"), "Ask AIRI to build, zone, or fix city services." },
                { ChatLocale.Id("Thinking"), "Thinking" },
                { ChatLocale.Id("Role.You"), "You" },
                { ChatLocale.Id("Role.Mayor"), "AIRI" },
                { ChatLocale.Id("Role.Error"), "Error" },
                { ChatLocale.Id("Tool.Running"), "Running" },
                { ChatLocale.Id("Tool.Done"), "Done" },
                { ChatLocale.Id("Tool.Error"), "Error" },
                { ChatLocale.Id("Tool.Interrupted"), "Interrupted" },
                { ChatLocale.Id("Tool.Arguments"), "Arguments" },
                { ChatLocale.Id("Tool.Result"), "Result" },
                { ChatLocale.Id("Tool.NoResult"), "No result yet." },
                { ChatLocale.Id("Status.Idle"), "Idle" },
                { ChatLocale.Id("Status.Thinking"), "Thinking" },
                { ChatLocale.Id("Status.Working"), "Working" },
                { ChatLocale.Id("Status.Interrupted"), "Interrupted" },
                { ChatLocale.Id("Status.Error"), "Error" },
                { ChatLocale.Id("Status.Queued"), "queued" },
                { ChatLocale.Id("Status.Vision"), "vision" },
                { ChatLocale.Id("Plan.None"), "No active plan" },
            };
        }

        public void Unload() { }
    }
}
