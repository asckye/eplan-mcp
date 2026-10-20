using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Safety;

/// <summary>
/// The approval dialog's texts (M3 design 4.6), in Chinese or English after the user interface language as
/// <c>doctor</c> chooses it; the engineer reads them, the model never does. Project data in the change list is shown
/// as it is, but on one line per change, without control or bidirectional characters (so it reads as what it is) and
/// shortened past <see cref="MaxValueLength"/> characters.
/// </summary>
internal static class ApprovalTexts
{
    public const int MaxValueLength = 120;

    /// <summary>The dialog for <paramref name="question"/>, without its binding and deadline.</summary>
    public static ApprovalAskArgs For(ApprovalQuestion question, CultureInfo culture)
    {
        bool zh = culture.TwoLetterISOLanguageName == "zh";
        if (question.Release is { } suspension)
        {
            return Release(suspension, zh);
        }

        WritePlan plan = question.Plan;
        string count = plan.Changes.Count.ToString(CultureInfo.InvariantCulture);
        return new ApprovalAskArgs
        {
            Title = zh ? "批准 AI 修改 EPLAN 项目？" : "Approve AI changes to the EPLAN project?",
            Summary = zh
                ? $"工具：{question.Tool}\n项目：{Value(plan.Project, zh)}（{Value(plan.ProjectPath, zh)}）\n将进行以下修改（共 {count} 处）："
                : $"Tool: {question.Tool}\nProject: {Value(plan.Project, zh)} ({Value(plan.ProjectPath, zh)})\nThe following changes will be made ({count}):",
            Changes = [.. plan.Changes.Select(c => Change(c, zh, plan.Details))],
            Note = zh
                ? $"批准后可在 EPLAN 中按 Ctrl+Z 撤销“AI: {question.Tool}”这一步。"
                : $"After approval, Ctrl+Z in EPLAN undoes the step \"AI: {question.Tool}\".",
            ApproveLabel = zh ? "批准" : "Approve",
            RejectLabel = zh ? "拒绝" : "Reject",
            CountdownFormat = zh ? "{0} 后自动拒绝" : "Rejected automatically in {0}",
        };
    }

    /// <summary>
    /// The confirmation that lifts a suspension (ADR-0010 section 3). Not "use Ctrl+Z": after EPLAN was ended during a
    /// write, its changes so far stay in the project and the undo history is gone (spike S8).
    /// </summary>
    private static ApprovalAskArgs Release(Suspension suspension, bool zh)
    {
        string time = suspension.Time == DateTimeOffset.MinValue ? "?" : suspension.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        string pid = suspension.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? "?";
        string scope = suspension.Project is { } project
            ? (zh ? "项目：" : "Project: ") + Value(project, zh)
            : zh ? $"范围：这个 EPLAN 实例（进程 {pid}）中的所有项目" : $"Scope: every project in this EPLAN instance (process {pid})";
        return new ApprovalAskArgs
        {
            Title = zh ? "上次 AI 修改的结果未知" : "The outcome of the last AI change is unknown",
            Summary = zh
                ? $"时间：{time}\n工具：{suspension.Tool}\n{scope}\n这次修改被中断，无法确定是否已经执行，项目中可能只有其中一部分。AI 的写入因此暂停。计划中的修改（最多 {SuspensionStore.MaxChanges} 处）："
                : $"Time: {time}\nTool: {suspension.Tool}\n{scope}\nThis change was interrupted and whether it was made is unknown; the project may contain part of it. AI writes are suspended. Planned changes (up to {SuspensionStore.MaxChanges}):",
            Changes = suspension.Changes.Count == 0
                ? [zh ? "（暂停记录中没有变更清单）" : "(the suspension record lists no changes)"]
                : [.. suspension.Changes.Select(c => Value(c, zh))],
            Note = zh
                ? "请先在 EPLAN 中核实项目：手动删除不完整的修改，或者恢复项目的副本；EPLAN 打开项目时如果提示检查数据，请执行检查。核实之后再继续，否则请保持暂停。"
                : "Check the project in EPLAN first: remove any partial change by hand, or restore a copy of the project, and run EPLAN's data check if it offers one when opening the project. Continue only after that; otherwise keep writes suspended.",
            ApproveLabel = zh ? "已核实，继续" : "Checked, continue",
            RejectLabel = zh ? "保持暂停" : "Keep suspended",
            CountdownFormat = zh ? "{0} 后自动保持暂停" : "Stays suspended automatically in {0}",
        };
    }

    /// <summary>One change as one line, in the dialog's language; <paramref name="details"/> are the plan's.</summary>
    internal static string Change(PlannedChange change, bool zh, IReadOnlyDictionary<string, string>? details = null)
    {
        string kind = zh ? change.Kind switch { "page" => "页", "device" => "设备", _ => change.Kind } : change.Kind;
        string property = zh ? change.Property switch { "description" => "描述", "tag" => "标识", _ => change.Property ?? string.Empty } : change.Property ?? string.Empty;
        string name = Value(change.Name, zh);
        return change.Action switch
        {
            // A macro's change names the page; what goes there is its value (macro.insert.plan).
            "create" when change.Kind == "macro" => zh ? $"在页 {name} 上插入宏：{Value(change.NewValue ?? string.Empty, zh)}" : $"Insert a macro on page {name}: {Value(change.NewValue ?? string.Empty, zh)}",
            "create" => zh ? $"新建{kind} {name}" : $"Create {kind} {name}",
            // Only EPLAN can tell which connections it adds or removes (eplan_connection_generate, tool spec 5).
            _ when change.Property == "connections" => zh
                ? $"生成{kind} {name} 上的连接（现有 {Value(change.OldValue ?? "?", zh)} 个；可能新增或删除连接）"
                : $"Generate connections on {kind} {name} (now {Value(change.OldValue ?? "?", zh)}; may add or remove connections)",
            // The check replaces all the project's check messages; the scope is in the plan's details (check.run.plan).
            _ when change.Property == "check messages" => CheckRun(change, zh, details),
            _ => zh
                ? $"修改{kind} {name} 的{property}：{Quoted(change.OldValue, zh)} → {Quoted(change.NewValue, zh)}"
                : $"Change {property} of {kind} {name}: {Quoted(change.OldValue, zh)} → {Quoted(change.NewValue, zh)}",
        };
    }

    private static string CheckRun(PlannedChange change, bool zh, IReadOnlyDictionary<string, string>? details)
    {
        string pages = Value(details?.GetValueOrDefault("pageCount") ?? "?", zh);
        string existing = Value(change.OldValue ?? "?", zh);
        string? scheme = details?.GetValueOrDefault("scheme") is { } given ? Value(given, zh) : null;
        return zh
            ? $"运行检查：{pages} 页（{(scheme is null ? "EPLAN 当前的方案" : "方案：" + scheme)}），替换现有的 {existing} 条检查消息"
            : $"Run the check on {pages} pages ({(scheme is null ? "EPLAN's current scheme" : "scheme: " + scheme)}), replacing the {existing} existing check messages";
    }

    private static string Quoted(string? value, bool zh) => value is null ? (zh ? "（未设置）" : "(not set)") : "'" + Value(value, zh) + "'";

    /// <summary>
    /// Project text on one line (control characters become spaces), without the bidirectional controls that make text
    /// read differently from what it contains (as <see cref="ResultShaper.CleanText"/>), shortened with its length named.
    /// </summary>
    private static string Value(string text, bool zh)
    {
        string clean = new([.. text.Where(c => c is not ((>= '‪' and <= '‮') or (>= '⁦' and <= '⁩'))).Select(c => char.IsControl(c) ? ' ' : c)]);
        if (clean.Length <= MaxValueLength)
        {
            return clean;
        }

        int cut = char.IsHighSurrogate(clean[MaxValueLength - 1]) ? MaxValueLength - 1 : MaxValueLength;
        string total = clean.Length.ToString(CultureInfo.InvariantCulture);
        return clean[..cut] + (zh ? $"…（共 {total} 个字符）" : $"… ({total} characters)");
    }
}
