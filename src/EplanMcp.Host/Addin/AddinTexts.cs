using System.Diagnostics;
using System.Globalization;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Addin;

/// <summary>
/// install-addin's and uninstall-addin's texts in Chinese or English, for the engineer and the Workbench (architecture
/// 8.11); the model never sees them. <see cref="AddinIssue.Detail"/> is quoted as found, in English.
/// </summary>
internal static class AddinTexts
{
    public static string Outcome(AddinOutcome outcome, bool zh) => outcome switch
    {
        AddinOutcome.Registered => zh ? "已注册" : "registered",
        AddinOutcome.Unregistered => zh ? "已撤销" : "unregistered",
        AddinOutcome.NotConfirmed => zh ? "未确认" : "not confirmed",
        AddinOutcome.Failed => zh ? "失败" : "failed",
        AddinOutcome.Refused => zh ? "拒绝" : "refused",
        AddinOutcome.Skipped => zh ? "跳过" : "skipped",
        _ => throw new UnreachableException($"Outcome {outcome} has no text."),
    };

    public static string Problem(AddinIssue issue, AddinAction action, bool zh)
    {
        string d = issue.Detail;
        bool install = action == AddinAction.Install;
        return issue.Problem switch
        {
            AddinProblem.AddinNotFound => zh
                ? $"没有找到插件，没有注册任何变体。找过：{d}。安装版的插件在 eplanmcp.exe 旁的 bridge\\net481\\ 中；开发时先用与 eplanmcp 相同的配置构建 src\\EplanMcp.Bridge.Eplan"
                : $"The add-in was not found, so nothing was registered. Looked for: {d}. An installed add-in is in bridge\\net481\\ next to eplanmcp.exe; in development, build src\\EplanMcp.Bridge.Eplan in the configuration of eplanmcp first",
            AddinProblem.NoEplan => zh
                ? $"在 {d} 下没有找到支持范围 {EplanVersions.Range} 内装有变体的 EPLAN，没有注册任何变体{(issue.Installed is { } found ? $"（找到的版本：{found.Replace(", ", "、", StringComparison.Ordinal)}）" : string.Empty)}"
                : $"No EPLAN in the supported range {EplanVersions.Range} with an installed variant under {d}, so nothing was registered{(issue.Installed is { } versions ? $" (versions found: {versions})" : string.Empty)}",
            AddinProblem.VariantNotFound => zh
                ? $"支持范围内的 EPLAN 中没有安装这些变体，没有注册任何变体：{d}（已安装：{issue.Installed?.Replace(", ", "、", StringComparison.Ordinal) ?? "无"}）"
                : $"No EPLAN in the supported range has these variants, so nothing was registered: {d} (installed: {issue.Installed ?? "none"})",
            AddinProblem.UnblockFailed => zh
                ? $"无法去掉插件文件的下载标记（带标记的插件 EPLAN 无法加载），没有注册任何变体：{d}"
                : $"Cannot remove the download mark from the add-in's files (EPLAN cannot load a marked add-in), so nothing was registered: {d}",
            AddinProblem.InvalidRecord => zh
                ? $"无法读取注册记录，请修复或删除这个文件后重新运行（删除后，卸载时不会撤销之前的注册）：{d}"
                : $"Cannot read the record of registrations; fix or delete it and run again (once deleted, uninstalling no longer undoes the earlier registrations): {d}",
            AddinProblem.EplanRunning => zh
                ? $"EPLAN 正在运行（进程 {d}）：这时执行命令会另起一个装着全部插件的 EPLAN；运行中的 EPLAN 是哪个变体判断不出来，所以有任何 EPLAN 在运行时都不执行。请先关闭所有 EPLAN，然后重新运行"
                : $"EPLAN is running (process {d}): the command would start a second EPLAN with every add-in, and which variant a running EPLAN is cannot be told, so nothing runs while any EPLAN does. Close every EPLAN and run again",
            AddinProblem.StartFailed => zh ? $"无法启动 EPLAN：{d}" : $"Cannot start EPLAN: {d}",
            AddinProblem.EplanExited => zh
                ? $"EPLAN（进程 {d}）在加载插件之前就退出了，没有确认{(install ? "注册，没有记录" : "撤销，记录保留")}：如果关掉了登录或授权对话框，请重新运行"
                : $"EPLAN (process {d}) exited before it loaded the add-in, so the {(install ? "registration is not confirmed and not recorded" : "unregistration is not confirmed and the record is kept")}: if a sign-in or licence dialog was closed, run again",
            AddinProblem.NotLoaded => zh
                ? $"EPLAN（进程 {d}）没有在等待时间内启动完成（插件没有加载），所以没有关闭它：如果它显示登录、授权对话框或系统消息窗口，请处理后等它启动完成，再自己关闭 EPLAN，然后运行 eplanmcp doctor 检查{(install ? string.Empty : "；记录保留，可以再运行一次 uninstall-addin")}"
                : $"EPLAN (process {d}) did not finish starting in time (the add-in did not load), so it was left open: if it shows a sign-in or licence dialog or a system messages window, deal with it and let EPLAN start, close EPLAN yourself, then check with eplanmcp doctor{(install ? string.Empty : "; the record is kept, so uninstall-addin can be run again")}",
            AddinProblem.PathMismatch => zh
                ? $"EPLAN 从另一个位置加载了插件：{d}。那里的旧注册仍然有效，这次的注册没有生效"
                : $"EPLAN loaded the add-in from another path: {d}. The earlier registration still holds; this one did not take",
            AddinProblem.HandshakeFailed => zh
                ? $"EPLAN（进程 {d}）加载了插件，但插件没有回应握手，看不出加载的是哪一个注册，所以没有继续：请关闭所有 EPLAN 后重新运行，仍然这样时用 eplanmcp doctor 查看插件"
                : $"EPLAN (process {d}) loaded the add-in, but the add-in did not answer the handshake, so which registration loaded cannot be told and nothing more was done: close every EPLAN and run again, and if it happens again, check the add-in with eplanmcp doctor",
            AddinProblem.RecordFailed => zh
                ? $"无法更新注册记录，{(install ? "卸载时不会撤销这一项" : "下次 uninstall-addin 还会再撤销一次")}：{d}"
                : $"Cannot update the record of registrations, so {(install ? "uninstalling will not undo this one" : "the next uninstall-addin undoes it again")}: {d}",
            _ => throw new UnreachableException($"Problem {issue.Problem} has no text."),
        };
    }

    public static string Note(AddinNote note, bool zh) => note switch
    {
        AddinNote.VersionUntested => zh
            ? $"这个版本在支持范围 {EplanVersions.Range} 内，但尚未实测（ADR-0009）"
            : $"This version is in the supported range {EplanVersions.Range} but not tested yet (ADR-0009)",
        AddinNote.UnsupportedVersion => zh
            ? $"不在支持范围 {EplanVersions.Range} 内，没有注册（ADR-0009）"
            : $"Outside the supported range {EplanVersions.Range}, so nothing was registered (ADR-0009)",
        AddinNote.EplanMissing => zh ? "这个 EPLAN 已经不在了，没有可撤销的注册，记录已删除" : "This EPLAN is gone, so there is nothing to unregister; the record is removed",
        AddinNote.CloseEplan => zh
            ? "这个 EPLAN 没有按要求关闭，请自己关闭它；它显示了系统消息窗口时，请先关闭那个窗口"
            : "This EPLAN did not close when asked; close it yourself, first closing its system messages window if it shows one",
        _ => throw new UnreachableException($"Note {note} has no text."),
    };

    public static string Starting(AddinAction action, AddinResult result, TimeSpan wait, bool zh)
    {
        string seconds = wait.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture);
        return zh
            ? $"EPLAN {result.Version} {result.Variant}：正在启动 EPLAN {(action == AddinAction.Install ? "注册" : "撤销")}插件，最多等 {seconds} 秒……"
            : $"EPLAN {result.Version} {result.Variant}: starting EPLAN to {(action == AddinAction.Install ? "register" : "unregister")} the add-in; this can take up to {seconds} s...";
    }

    public static string Addin(bool zh) => zh ? "插件：" : "add-in: ";

    public static string Process(int processId, bool zh)
    {
        string pid = processId.ToString(CultureInfo.InvariantCulture);
        return zh ? $"（进程 {pid}）" : $" (process {pid})";
    }

    public static string NothingRecorded(bool zh) => zh ? "没有注册记录，不需要撤销" : "No registrations recorded, nothing to undo";
}
