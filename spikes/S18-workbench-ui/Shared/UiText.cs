using System.Globalization;

namespace EplanMcp.Spikes.S18;

/// <summary>Interface language of the prototype. Switching it re-applies every text at once (ADR-0015 item 6).</summary>
internal enum UiLanguage
{
    Chinese,
    English,
}

/// <summary>
/// The single text table of the approval card: one key, both languages. Neither prototype uses .resw or .resx, so
/// switching the language needs no restart and no system language setting.
/// </summary>
internal static class UiText
{
    private static readonly Dictionary<string, (string Zh, string En)> _table = new(StringComparer.Ordinal)
    {
        ["Window.Title"] = ("EplanMcp Workbench：S18 原型 {0}", "EplanMcp Workbench: S18 prototype {0}"),
        ["Tray.Tip"] = ("EplanMcp Workbench（S18 原型 {0}）：1 个问题等待回答", "EplanMcp Workbench (S18 prototype {0}): 1 question waiting"),
        ["Proto.Language"] = ("语言", "Language"),
        ["Proto.Theme"] = ("主题", "Theme"),
        ["Proto.ThemeSystem"] = ("跟随系统", "System"),
        ["Proto.ThemeLight"] = ("浅色", "Light"),
        ["Proto.ThemeDark"] = ("深色", "Dark"),
        ["Proto.ThemeHighContrast"] = ("高对比度（TIA 调色板）", "High contrast (TIA palette)"),
        ["Card.State"] = ("等待批准", "Waiting"),
        ["Card.UnverifiedTag"] = ("未验证", "unverified"),
        ["Card.Host"] = ("eplanmcp {0} · PID {1}", "eplanmcp {0} · PID {1}"),
        ["Card.AutoReject"] = ("到期自动拒绝", "Rejected automatically at the deadline"),
        ["Card.Parameters"] = ("参数", "Parameters"),
        ["Card.RiskWrite"] = ("Write · 批准后可在 EPLAN 中按 Ctrl+Z 撤销", "Write · Ctrl+Z in EPLAN undoes it"),
        ["Card.RiskReal"] = ("真实项目，不在 scratch 中", "Real project, not in scratch"),
        ["Card.Announce"] = ("剩余 {0} 秒", "{0} seconds left"),
        ["Op.Update"] = ("修改", "Update"),        ["Card.Title"] = ("批准 AI 修改 EPLAN 项目？", "Approve the AI's change to the EPLAN project?"),
        ["Card.Tool"] = ("工具", "Tool"),
        ["Card.Tier"] = ("级别", "Tier"),
        ["Card.Project"] = ("项目", "Project"),
        ["Card.Instance"] = ("EPLAN 实例", "EPLAN instance"),
        ["Card.Client"] = ("发起的客户端", "Client"),
        ["Card.Unverified"] = ("{0}（未验证）", "{0} (unverified)"),
        ["Card.Changes"] = ("将进行以下修改", "These changes will be made"),
        ["Card.ChangesSource"] = ("来自项目的数据，按纯文本显示", "Data from the project, shown as plain text"),
        ["Card.Removed"] = ("已剥除 {0} 个控制字符或双向文字控制符", "Removed {0} control or bidirectional formatting characters"),
        ["Card.MoreLines"] = ("还有 {0} 行", "{0} more lines"),
        ["Card.Undo"] = ("批准后可在 EPLAN 中按 Ctrl+Z 撤销“AI: {0}”这一步。", "After approval, Ctrl+Z in EPLAN undoes the step \"AI: {0}\"."),
        ["Card.Countdown"] = ("剩余 {0}，到期自动拒绝", "{0} left, then rejected automatically"),
        ["Card.CountdownName"] = ("剩余时间", "Time left"),
        ["Card.Expired"] = ("已过期，已自动拒绝", "Expired, rejected automatically"),
        ["Card.Approve"] = ("批准", "Approve"),
        ["Card.Reject"] = ("拒绝", "Reject"),
        ["Card.Approved"] = ("已批准（原型：不执行任何操作）", "Approved (prototype: nothing runs)"),
        ["Card.Rejected"] = ("已拒绝（原型：不执行任何操作）", "Rejected (prototype: nothing runs)"),
    };

    internal static IEnumerable<string> Keys => _table.Keys;

    /// <summary>BCP 47 tag set on the window so the text stack picks the font fallback for that language.</summary>
    internal static string LanguageTag(UiLanguage language) => language == UiLanguage.Chinese ? "zh-CN" : "en-US";

    internal static string Get(string key, UiLanguage language)
    {
        (string zh, string en) = _table[key];
        return language == UiLanguage.Chinese ? zh : en;
    }

    internal static string Format(string key, UiLanguage language, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, Get(key, language), args);
}
