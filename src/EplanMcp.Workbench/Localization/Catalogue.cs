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
        ("Shell.CloseHint", "Close to the tray; the Workbench keeps taking approval questions.", "关闭到托盘：Workbench 继续接收审批问题。"),

        // Whether approval questions are taken (STS-11), in the Header.
        ("Pipe.Accepting", "Taking approval questions", "正在接收审批问题"),
        ("Pipe.Full", "Taking no new questions: {0} are waiting", "不接收新问题：已有 {0} 个在等待"),
        ("Pipe.FullHelp", "New questions are asked in the EPLAN dialog until some of these are answered.", "在回答其中一些之前，新的问题改由 EPLAN 中的对话框询问。"),
        ("Pipe.Taken", "Taking no approval questions: the pipe name is in use", "不接收审批问题：管道名已被占用"),
        ("Pipe.TakenHelp", "Another program holds the approval pipe name of this user and data root, so approvals are asked in the EPLAN dialog. Close that program and start the Workbench again; logs\\workbench-*.log in the data root has the details.", "另一个程序占用了这个用户和数据根目录的审批管道名，审批因此改由 EPLAN 中的对话框询问。请关闭那个程序后重新启动 Workbench；详细信息见数据根目录中的 logs\\workbench-*.log。"),

        // Tray (STS-09, NAV-09) and leaving with questions waiting (DLG-11).
        ("Tray.Open", "Open Workbench", "打开 Workbench"),
        ("Tray.Exit", "Exit", "退出"),
        ("Tray.Pending", "{0} questions wait for approval", "{0} 个问题等待审批"),
        ("Tray.Arrived", "{0} · new: {1}", "{0} · 新问题：{1}"),
        ("Exit.Title", "Exit the Workbench?", "退出 Workbench？"),
        ("Exit.Message", "{0} questions are waiting. If you exit, they return to the AI as cancelled and nothing is changed.", "有 {0} 个问题在等待回答。退出后它们按“已取消”返回给 AI，不执行任何修改。"),
        ("Exit.Cancel", "_Cancel", "取消(_C)"),
        ("Exit.Confirm", "E_xit", "退出(_X)"),

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
        // A question (APR-07 to APR-12); the titles, buttons, countdowns and notes are the EPLAN dialog's (TXT-09).
        ("Question.Title", "Approve AI changes to the EPLAN project?", "批准 AI 修改 EPLAN 项目？"),
        ("Question.ReleaseTitle", "The outcome of the last AI change is unknown", "上次 AI 修改的结果未知"),
        ("Question.Approval", "Approval", "审批"),
        ("Question.Release", "Release of the write suspension", "解除写入暂停"),
        ("Question.Approve", "Approve", "批准"),
        ("Question.Reject", "Reject", "拒绝"),
        ("Question.Continue", "Checked, continue", "已核实，继续"),
        ("Question.KeepSuspended", "Keep suspended", "保持暂停"),
        ("Question.Countdown", "Rejected automatically in {0}", "{0} 后自动拒绝"),
        ("Question.ReleaseCountdown", "Stays suspended automatically in {0}", "{0} 后自动保持暂停"),
        ("Question.TimeLeft", "Time left", "剩余时间"),
        ("Question.Tool", "Tool", "工具"),
        ("Question.Tier", "Tier", "级别"),
        ("Question.Write", "Write", "写入"),
        ("Question.Destructive", "Destructive", "破坏性"),
        ("Question.Project", "Project", "项目"),
        ("Question.Eplan", "EPLAN instance", "EPLAN 实例"),
        ("Question.EplanValue", "{0} · {1} · process {2}", "{0} · {1} · 进程 {2}"),
        ("Question.Client", "Client (unverified)", "客户端（未验证）"),
        ("Question.NoClient", "Not given", "未提供"),
        ("Question.Unknown", "Unknown", "未知"),
        ("Question.Changes", "Change list", "变更清单"),
        ("Question.FromProject", "Data from the project", "来自项目的数据"),
        ("Question.MoreLines", "… {0} more lines", "……还有 {0} 行"),
        ("Question.Undo", "After approval, Ctrl+Z in EPLAN undoes the step \"AI: {0}\".", "批准后可在 EPLAN 中按 Ctrl+Z 撤销“AI: {0}”这一步。"),
        ("Question.ReleaseNote", "Check the project in EPLAN first: remove any partial change by hand, or restore a copy of the project, and run EPLAN's data check if it offers one when opening the project. Continue only after that; otherwise keep writes suspended.", "请先在 EPLAN 中核实项目：手动删除不完整的修改，或者恢复项目的副本；EPLAN 打开项目时如果提示检查数据，请执行检查。核实之后再继续，否则请保持暂停。"),
        ("Question.DestructiveBanner", "Destructive operation: the project is backed up first", "破坏性操作：执行前先备份项目"),
        ("Question.BackupTo", "Backed up first to {0}", "执行前先备份到 {0}"),
        ("Question.NoBackup", "The Host named no backup folder.", "Host 没有给出备份目录。"),
        ("Question.Suspended", "Suspended write", "暂停的写入"),
        ("Question.SuspendedValue", "{0} · {1} · {2}", "{0} · {1} · {2}"),
        ("Question.ScopeProject", "Project {0}", "项目 {0}"),
        ("Question.ScopeInstance", "Every project in EPLAN process {0}", "EPLAN 进程 {0} 中的所有项目"),
        ("Question.ItemName", "{0}, {1}, project {2}, {3} left", "{0}，{1}，项目 {2}，还剩 {3}"),
        ("Question.AnsweredName", "{0}, project {1}, {2}", "{0}，项目 {1}，{2}"),
        ("Question.Arrived", "New question: {0}", "新问题：{0}"),
        ("Question.SecondsLeft", "{0} seconds left", "还剩 {0} 秒"),

        // How a question ended and what became of the write (APR-11, APR-12).
        ("Result.Approved", "Approved", "已批准"),
        ("Result.Rejected", "Rejected", "已拒绝"),
        ("Result.Continued", "Continued", "已继续"),
        ("Result.KeptSuspended", "Kept suspended", "保持暂停"),
        ("Result.Expired", "Expired", "已过期"),
        ("Result.Cancelled", "Cancelled: the client cancelled the call", "已取消：客户端取消了调用"),
        ("Result.Disconnected", "Cancelled: the connection broke", "已取消：连接断开"),
        ("Result.Closed", "Cancelled: the Workbench quit", "已取消：Workbench 退出"),
        ("Outcome.Running", "Running", "执行中"),
        ("Outcome.Committed", "Done", "已完成"),
        ("Outcome.RolledBack", "Rolled back", "已回滚"),
        ("Outcome.NotStarted", "Not run", "未执行"),
        ("Outcome.Unknown", "Outcome unknown: writes are suspended, and the next write asks you to confirm first", "结果未知：写入已暂停，下一次写入前会请你确认"),
        ("Outcome.NotReceived", "No result received", "未收到结果"),
        ("Outcome.Because", "{0} ({1})", "{0}（{1}）"),

        ("Approvals.Groups", "Questions", "问题"),
        ("Approvals.Waiting", "Waiting ({0})", "等待中（{0}）"),
        ("Approvals.Answered", "Answered ({0})", "已回答（{0}）"),
        ("Approvals.List", "Waiting questions", "等待中的问题"),
        ("Approvals.AnsweredList", "Answered questions", "已回答的问题"),
        ("Approvals.Detail", "The question shown", "显示的问题"),
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
