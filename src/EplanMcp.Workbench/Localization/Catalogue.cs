namespace EplanMcp.Workbench.Localization;

/// <summary>
/// Every text the Workbench shows or reads out (workbench-ui LOC-01), as one table of (key, English, Chinese): a key
/// cannot exist in one language only. StringsTests checks the rest: no key twice, nothing blank, the same placeholders
/// in both languages. The vendored Loc reads it through TIA's <c>Strings</c> (TiaStubs\Strings.cs).
/// </summary>
internal static class Catalogue
{
    public static IReadOnlyList<(string Key, string En, string Zh)> Entries { get; } =
    [
        ("App.Name", "EplanMcp Workbench", "EplanMcp Workbench"),

        // Shell: Sider, Header, window (workbench-ui 3.1, NAV-03).
        ("Shell.WindowTitle", "{0} – EplanMcp Workbench", "{0} – EplanMcp Workbench"),
        ("Shell.Breadcrumb", "Workbench", "Workbench"),
        ("Shell.Shortcut", "{0} ({1})", "{0}（{1}）"),
        ("Shell.Menu", "Pages", "页面"),
        ("Shell.Header", "Header", "顶栏"),
        ("Shell.Content", "Page content", "页面内容"),
        ("Shell.Collapse", "Collapse the menu", "收起菜单"),
        ("Shell.Expand", "Expand the menu", "展开菜单"),
        ("Shell.Minimize", "Minimize", "最小化"),
        ("Shell.Maximize", "Maximize", "最大化"),
        ("Shell.Restore", "Restore", "还原"),
        ("Shell.Close", "Close", "关闭"),

        // The settings drawer: language and theme (section 7, LOC-10, COL-09); each language named in its own.
        ("Settings.Title", "Settings", "设置"),
        ("Settings.Close", "Close the settings", "关闭设置"),
        ("Settings.Language", "Language", "语言"),
        ("Settings.Chinese", "中文", "中文"),
        ("Settings.English", "English", "English"),
        ("Settings.Theme", "Theme", "主题"),
        ("Settings.ThemeSystem", "Follow Windows", "跟随系统"),
        ("Settings.ThemeLight", "Light", "浅色"),
        ("Settings.ThemeDark", "Dark", "深色"),
        ("Settings.HighContrast", "While Windows uses a contrast theme, the Workbench uses its colours.", "Windows 使用对比主题时，Workbench 使用对比主题的颜色。"),

        // Pages (workbench-ui section 7), their captions and the placeholders of the pages still to come (ERR-05).
        ("Page.Approvals", "Approvals", "审批队列"),
        ("Page.Audit", "Audit", "审计"),
        ("Page.Status", "Status", "状态"),
        ("Page.Clients", "MCP and clients", "MCP 与客户端"),
        ("Page.Approvals.Caption", "Questions from the AI that wait for your answer", "等待你回答的 AI 问题"),
        ("Page.Audit.Caption", "The audit records, read only", "只读查看审计记录"),
        ("Page.Status.Caption", "EPLAN, the add-in and the approval channels", "EPLAN、插件和审批渠道"),
        ("Page.Clients.Caption", "MCP client configuration and the virtual machine connection", "MCP 客户端配置和虚拟机连接"),
        ("Page.Later", "This page comes in a later version", "这一页将在后续版本提供"),
        ("Page.Audit.Later", "Until then, eplanmcp audit verify checks the audit records in the audit folder of the data root.", "在此之前，可以用 eplanmcp audit verify 校验数据根目录 audit 文件夹中的审计记录。"),
        ("Page.Status.Later", "Until then, eplanmcp doctor shows EPLAN, the add-in and which channel asks the next approval.", "在此之前，可以用 eplanmcp doctor 查看 EPLAN、插件，以及下一次审批由哪个渠道询问。"),
        ("Page.Clients.Later", "Until then, configure the MCP client as the README describes.", "在此之前，请按 README 中的步骤配置 MCP 客户端。"),
        ("Approvals.Empty", "No questions waiting", "没有等待中的问题"),
        ("Approvals.EmptyText", "When the AI wants to change a real project, its question shows here; while the Workbench is not running, the EPLAN dialog asks instead.", "AI 要修改真实项目时，问题会出现在这里；Workbench 不在运行时，改由 EPLAN 中的对话框询问。"),

        // Read by the vendored theme's scroll bars (ScrollCommandNameConverter, Primer.xaml).
        ("Access.Scroll", "Scroll", "滚动"),
        ("Access.ScrollUp", "Scroll up", "向上滚动"),
        ("Access.ScrollDown", "Scroll down", "向下滚动"),
        ("Access.ScrollLeft", "Scroll left", "向左滚动"),
        ("Access.ScrollRight", "Scroll right", "向右滚动"),
        ("Access.ScrollPosition", "Scroll position", "滚动位置"),
    ];

    public static IReadOnlyDictionary<string, string> English { get; } = Build(entry => entry.En);

    public static IReadOnlyDictionary<string, string> Chinese { get; } = Build(entry => entry.Zh);

    private static Dictionary<string, string> Build(Func<(string Key, string En, string Zh), string> pick)
    {
        var table = new Dictionary<string, string>(Entries.Count, StringComparer.Ordinal);
        foreach ((string Key, string En, string Zh) entry in Entries)
        {
            table[entry.Key] = pick(entry);
        }

        return table;
    }
}
