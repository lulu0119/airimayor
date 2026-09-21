using System.Collections.Generic;
using Colossal;

namespace CitiesSkylines2Agent
{
    public class LocaleZhHans : IDictionarySource
    {
        private readonly Setting m_Setting;

        public LocaleZhHans(Setting setting) { m_Setting = setting; }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Cities Skylines 2 Agent" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "主要" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kConnectionGroup), "连接" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kAgentGroup), "代理" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Endpoint)), "服务端地址" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Endpoint)), "OpenAI 兼容 API 基础地址。" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ApiKey)), "API 密钥" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ApiKey)), "仅保存在设置中。" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Model)), "模型" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Model)), "例如 gpt-5.6-sol、deepseek-v4-flash。" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.AutoStart)), "自动启动" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.AutoStart)), "加载城市时开始一轮。" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Continuous)), "持续运行" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Continuous)), "让代理不停地运行。" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.AllowProgressionPurchases)), "允许发展点购买" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.AllowProgressionPurchases)), "允许代理在发展树上花费已赚取的发展点。" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.AllowDemolition)), "允许拆除" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.AllowDemolition)), "允许代理在没有确认对话框的情况下推平建筑和路段。" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableDevelopmentTools)), "开发 / 验收工具" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableDevelopmentTools)), "向游戏内代理暴露诊断、实验和手动存档工具。" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.VisionTools)), "视觉工具" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.VisionTools)), "自动跟随模型名称的能力；开和关强制指定结果。" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Api)), "API 类型" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Api)), "Chat Completions 或 Responses。这是循环的请求形状，不是服务端模型上限。" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.WindowTokens)), "窗口 token 数" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.WindowTokens)), "循环视为窗口的 token 数量。这不会改变服务端模型上限。" },

                { m_Setting.GetEnumValueLocaleID(VisionToolMode.Off), "关" },
                { m_Setting.GetEnumValueLocaleID(VisionToolMode.On), "开" },
                { m_Setting.GetEnumValueLocaleID(ApiKind.ChatCompletions), "Chat Completions" },
                { m_Setting.GetEnumValueLocaleID(ApiKind.Responses), "Responses" },

                { ChatLocale.Id("Title"), "城市代理" },
                { ChatLocale.Id("Composer.Loading"), "正在加载城市…" },
                { ChatLocale.Id("Composer.Ready"), "给市长留言…" },
                { ChatLocale.Id("Composer.Queued"), "输入以排队…" },
                { ChatLocale.Id("Composer.Send"), "发送" },
                { ChatLocale.Id("Composer.Stop"), "停止" },
                { ChatLocale.Id("Empty"), "让市长建造、分区或修复城市服务。" },
                { ChatLocale.Id("Thinking"), "思考中" },
                { ChatLocale.Id("Role.You"), "你" },
                { ChatLocale.Id("Role.Mayor"), "市长" },
                { ChatLocale.Id("Role.Error"), "错误" },
                { ChatLocale.Id("Tool.Running"), "运行中" },
                { ChatLocale.Id("Tool.Done"), "完成" },
                { ChatLocale.Id("Tool.Error"), "错误" },
                { ChatLocale.Id("Tool.Interrupted"), "已中断" },
                { ChatLocale.Id("Tool.Arguments"), "参数" },
                { ChatLocale.Id("Tool.Result"), "结果" },
                { ChatLocale.Id("Tool.NoResult"), "暂无结果。" },
                { ChatLocale.Id("Status.Idle"), "空闲" },
                { ChatLocale.Id("Status.Thinking"), "思考中" },
                { ChatLocale.Id("Status.Working"), "执行中" },
                { ChatLocale.Id("Status.Interrupted"), "已中断" },
                { ChatLocale.Id("Status.Error"), "错误" },
                { ChatLocale.Id("Status.Queued"), "条排队中" },
                { ChatLocale.Id("Status.Vision"), "视觉" },
                { ChatLocale.Id("Plan.None"), "没有当前计划" },
                { ChatLocale.Id("Settings.Title"), "设置" },
                { ChatLocale.Id("Settings.Connection.Title"), "连接" },
                { ChatLocale.Id("Settings.Connection.Endpoint"), "服务端地址" },
                { ChatLocale.Id("Settings.Connection.ApiKey"), "API 密钥" },
                { ChatLocale.Id("Settings.Connection.ShowKey"), "显示" },
                { ChatLocale.Id("Settings.Connection.HideKey"), "隐藏" },
                { ChatLocale.Id("Settings.Model.Title"), "模型" },
                { ChatLocale.Id("Settings.Model.Name"), "模型" },
                { ChatLocale.Id("Settings.Model.Hint"), "获取模型后选择，或直接输入名称" },
                { ChatLocale.Id("Settings.Model.Fetch"), "获取" },
                { ChatLocale.Id("Settings.Model.Fetching"), "获取中…" },
                { ChatLocale.Id("Settings.Model.Loaded"), "已加载 {{count}} 个模型" },
                { ChatLocale.Id("Settings.Actions.Save"), "保存" },
            };
        }

        public void Unload() { }
    }
}
