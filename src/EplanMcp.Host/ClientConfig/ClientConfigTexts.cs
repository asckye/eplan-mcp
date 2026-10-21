using System.Diagnostics;

namespace EplanMcp.Host.ClientConfig;

/// <summary>
/// client-config's texts in Chinese or English, for the engineer and the Workbench (architecture 8.11); the model never
/// sees them. <see cref="ClientIssue.Detail"/> is quoted as found, in English.
/// </summary>
internal static class ClientConfigTexts
{
    public static string Outcome(ClientOutcome outcome, bool zh) => outcome switch
    {
        ClientOutcome.Written => zh ? "已写入" : "written",
        ClientOutcome.WouldWrite => zh ? "将写入" : "would write",
        ClientOutcome.Removed => zh ? "已移除" : "removed",
        ClientOutcome.Unchanged => zh ? "未改变" : "unchanged",
        ClientOutcome.Failed => zh ? "失败" : "failed",
        _ => throw new UnreachableException($"Outcome {outcome} has no text."),
    };

    public static string Problem(ClientIssue issue, bool zh)
    {
        string d = issue.Detail;
        return issue.Problem switch
        {
            ClientProblem.InvalidFile => zh ? $"文件无法解析，没有改动：{d}" : $"The file cannot be parsed and was left unchanged: {d}",
            ClientProblem.UnsupportedToml => zh
                ? $"config.toml 中 MCP 服务器的写法无法逐行安全修改，没有改动：{d} 请把 eplan 写成 [mcp_servers.eplan] 表、单行的 command 和 args，或者手工删除它，然后重新运行"
                : $"config.toml defines MCP servers in a form a line edit cannot change safely, so it was left unchanged: {d} Write eplan as a [mcp_servers.eplan] table with one-line command and args, or remove it by hand, then run again",
            ClientProblem.AmbiguousPackage => zh ? $"有多个 Claude 桌面版的包，无法确定写入哪一个：{d}" : $"Cannot tell which Claude Desktop package to write: {d}",
            ClientProblem.FileChanged => zh
                ? "读取之后文件被其他程序改动过，没有写入：请完全退出这个客户端后重新运行"
                : "Another program changed the file after it was read, so nothing was written: quit the client fully and run again",
            ClientProblem.InvalidRecord => zh ? $"无法读取写入记录，请修复或删除这个文件后重新运行：{d}" : $"Cannot read the record of written entries; fix or delete it and run again: {d}",
            ClientProblem.RecordFailed => zh ? $"已完成，但无法更新写入记录，卸载时不会自动处理这一项：{d}" : $"Done, but the record of written entries could not be updated, so uninstalling will not handle this entry: {d}",
            ClientProblem.UnknownClient => zh ? $"记录中的客户端 {d} 不是这个版本支持的客户端，没有移除" : $"The recorded client {d} is not one this version supports; nothing was removed",
            ClientProblem.IoError => zh ? $"无法读写文件：{d}" : $"Cannot read or write the file: {d}",
            ClientProblem.NotEplanmcp => zh
                ? $"client-config 要由 eplanmcp.exe 本身运行，配置中写的是它的完整路径；当前进程是 {d}"
                : $"client-config must run as eplanmcp.exe itself, whose full path the entry gets; this process is {d}",
            ClientProblem.PluginInstalled => zh
                ? $"已经安装了 Claude Code 插件 {d}，它已经提供 eplan 服务器；再写一份相同的配置，Claude Code 只会启动其中一个。保留插件即可；要改用这里的配置，先用 /plugin uninstall 卸载插件"
                : $"The Claude Code plugin {d} is installed and already provides the eplan server; with a second entry for the same command Claude Code starts only one of them. Keep the plugin, or uninstall it (/plugin uninstall) to use this entry instead",
            ClientProblem.ClaudeDesktopRunning => zh
                ? "Claude 桌面版正在运行，它会频繁改写这个文件，没有改动：请从托盘图标完全退出 Claude 桌面版后重新运行"
                : "Claude Desktop is running and rewrites this file often, so it was left unchanged: quit Claude Desktop fully (from its tray icon) and run again",
            ClientProblem.MsixRedirected => zh
                ? $"当前进程在 MSIX 包 {d} 的环境中（例如由 Claude 桌面版启动），写入 %APPDATA% 会被重定向，所以没有改动任何配置：请从开始菜单、Workbench 或普通的终端运行"
                : $"This process runs in the environment of MSIX package {d} (started by Claude Desktop, for example), where writes to %APPDATA% are redirected, so no configuration was changed: run it from the Start menu, the Workbench or an ordinary terminal",
            _ => throw new UnreachableException($"Problem {issue.Problem} has no text."),
        };
    }

    public static string CommentsDropped(bool zh) => zh
        ? "改写会丢弃文件中的 JSONC 注释，原文件保存在备份中"
        : "Rewriting drops the file's JSONC comments; the original is kept in the backup";

    public static string Backup(bool zh) => zh ? "备份：" : "backup: ";

    public static string Before(bool zh) => zh ? "改动前：" : "before:";

    public static string After(bool zh) => zh ? "改动后：" : "after:";

    public static string None(bool zh) => zh ? "（没有）" : "(none)";

    public static string NothingRecorded(bool zh) => zh ? "没有记录的条目，不需要移除" : "No recorded entries to remove";

    public static string Restart(bool zh) => zh
        ? "完全退出并重新打开改动过的客户端后生效。"
        : "Quit the changed clients fully and open them again to load the change.";
}
