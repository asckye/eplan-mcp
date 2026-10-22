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
        string project = plan.ProjectPath.Length == 0
            ? string.Empty
            : zh ? $"项目：{Value(plan.Project, zh)}（{Value(plan.ProjectPath, zh)}）\n" : $"Project: {Value(plan.Project, zh)} ({Value(plan.ProjectPath, zh)})\n";
        return new ApprovalAskArgs
        {
            Title = Title(plan, zh),
            Summary = zh
                ? $"工具：{question.Tool}\n{MasterData(plan.MasterData, zh)}{project}将进行以下修改（共 {count} 处）："
                : $"Tool: {question.Tool}\n{MasterData(plan.MasterData, zh)}{project}The following changes will be made ({count}):",
            Changes = [.. plan.Changes.Select(c => Change(c, zh, plan.Details)), .. Orphaned(plan, zh), .. ToNumber(plan, zh), .. Numbering(plan, zh), .. Translations(plan, zh), .. Translating(plan, zh)],
            Note = Backup(question.BackupFolder, zh) + MasterDataBackup(question.MasterDataBackupFolder, zh)
                + (plan.MasterData is null ? Undo(question.Tool, plan, zh) : MasterDataUndo(plan, zh)),
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
        string scope = suspension.Database is { } database
            ? (zh ? "部件库：" : "Parts database: ") + Value(database, zh)
            : suspension.Project is { } project
            ? (zh ? "项目：" : "Project: ") + Value(project, zh)
            : zh ? $"范围：这个 EPLAN 实例（进程 {pid}）中的所有项目" : $"Scope: every project in this EPLAN instance (process {pid})";
        bool parts = suspension.Database is not null;
        string where = parts ? (zh ? "部件库" : "the parts database") : (zh ? "项目" : "the project");
        return new ApprovalAskArgs
        {
            Title = zh ? "上次 AI 修改的结果未知" : "The outcome of the last AI change is unknown",
            Summary = zh
                ? $"时间：{time}\n工具：{suspension.Tool}\n{scope}\n这次修改被中断，无法确定是否已经执行，{where}中可能只有其中一部分。AI 的写入因此暂停。计划中的修改（最多 {SuspensionStore.MaxChanges} 处）："
                : $"Time: {time}\nTool: {suspension.Tool}\n{scope}\nThis change was interrupted and whether it was made is unknown; {where} may contain part of it. AI writes are suspended. Planned changes (up to {SuspensionStore.MaxChanges}):",
            Changes = suspension.Changes.Count == 0
                ? [zh ? "（暂停记录中没有变更清单）" : "(the suspension record lists no changes)"]
                : [.. suspension.Changes.Select(c => Value(c, zh))],
            Note = parts
                ? zh
                    ? "请先在 EPLAN 的部件管理中核实部件库；需要时关闭 EPLAN，按执行前的备份恢复部件库。核实之后再继续，否则请保持暂停。"
                    : "Check the parts database in EPLAN's parts management first; if needed, close EPLAN and restore the database from the backup made before the change. Continue only after that; otherwise keep writes suspended."
                : zh
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
        string kind = zh ? change.Kind switch { "page" => "页", "device" => "设备", "language" => "语言", _ => change.Kind } : change.Kind;
        string property = zh ? change.Property switch { "description" => "描述", "tag" => "标识", _ => change.Property ?? string.Empty } : change.Property ?? string.Empty;
        string name = Value(change.Name, zh);
        return change.Action switch
        {
            // A macro's change names the page; what goes there is its value (macro.insert.plan).
            "create" when change.Kind == "macro" => zh ? $"在页 {name} 上插入宏：{Value(change.NewValue ?? string.Empty, zh)}" : $"Insert a macro on page {name}: {Value(change.NewValue ?? string.Empty, zh)}",
            // A device's change names the page too; the part, the tag and the point are its value (device.place.plan).
            "create" when change.Kind == "device" => zh ? $"在页 {name} 上放置设备：{Value(change.NewValue ?? string.Empty, zh)}" : $"Place a device on page {name}: {Value(change.NewValue ?? string.Empty, zh)}",
            // An import names the file, its size and SHA-256 (import.dxf.plan; M4c design 4.5).
            "create" when change.Kind == "drawing" => zh ? $"在页 {name} 上导入图形：{Value(change.NewValue ?? string.Empty, zh)}" : $"Import a drawing on page {name}: {Value(change.NewValue ?? string.Empty, zh)}",
            "create" => zh ? $"新建{kind} {name}" : $"Create {kind} {name}",
            "delete" => (zh ? $"删除{kind} {name}" : $"Delete {kind} {name}") + (change.OldValue is null ? string.Empty : zh ? $"（{Value(change.OldValue, zh)}）" : $" ({Value(change.OldValue, zh)})"),
            // Only EPLAN can tell which connections it adds or removes (eplan_connection_generate, tool spec 5).
            // Only EPLAN's numbering rules give the new tags (eplan_device_renumber, tool spec 5): the scope, worded by the Bridge.
            _ when change.Property == "device tags" => (zh ? "重新编号：" : "Renumber ") + Value(change.OldValue ?? string.Empty, zh),
            _ when change.Property == "page number" => zh ? $"重新编号{kind} {name}" : $"Renumber {kind} {name}",
            // The numbering tools of M4c (terminals, cables, wire numbers): the scope, worded by the Bridge.
            _ when change.Property == "numbering" => (zh ? "重新编号：" : "Renumber ") + Value(change.OldValue ?? string.Empty, zh),
            // Only the translation database tells which texts it fills in (eplan_project_translate, tool spec 5): the scope, worded by the Bridge.
            _ when change.Property == "translations" => (zh ? "按翻译库翻译：" : "Translate with the translation database: ") + Value(change.OldValue ?? string.Empty, zh),
            _ when change.Property == "connections" => zh
                ? $"生成{kind} {name} 上的连接（现有 {Value(change.OldValue ?? "?", zh)} 个；可能新增或删除连接）"
                : $"Generate connections on {kind} {name} (now {Value(change.OldValue ?? "?", zh)}; may add or remove connections)",
            // The check replaces all the project's check messages; the scope is in the plan's details (check.run.plan).
            _ when change.Property == "check messages" => CheckRun(change, zh, details),
            // Only EPLAN can tell which report pages it creates or removes (eplan_report_update, tool spec 5; M4 design Q7).
            _ when change.Property == "reports" => zh
                ? $"更新全部报表（现有报表页 {Value(change.OldValue ?? "?", zh)} 页）；可能新建、替换或删除报表页"
                : $"Update all reports ({Value(change.OldValue ?? "?", zh)} report pages now); may create, replace or delete report pages",
            _ when change.Property == "report" => zh ? $"更新报表页 {name}" : $"Update report page {name}",
            _ when change.Property == "languages" => zh
                ? $"修改项目 {name} 的语言：{Quoted(change.OldValue, zh)} → {Quoted(change.NewValue, zh)}"
                : $"Change the languages of project {name}: {Quoted(change.OldValue, zh)} → {Quoted(change.NewValue, zh)}",
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

    /// <summary>
    /// After the change list, the devices a page deletion leaves without their main function (page.delete.plan's
    /// <c>orphaned</c> details: tag, functions left, pages, tab-separated), under their own heading (tool spec
    /// eplan_page_delete, section 5); nothing for other plans.
    /// </summary>
    private static IEnumerable<string> Orphaned(WritePlan plan, bool zh)
    {
        if (plan.Details?.GetValueOrDefault("orphaned") is not { Length: > 0 } lines)
        {
            yield break;
        }

        yield return string.Empty;
        yield return zh ? "删除后这些设备在其他页上的功能将失去主功能：" : "After the deletion, these devices keep functions on other pages without their main function:";
        string[] devices = lines.Split('\n');
        foreach (string[] fields in devices.Select(line => line.Split('\t')))
        {
            string tag = Value(fields[0], zh);
            string left = fields.Length > 1 ? fields[1] : "?";
            string pages = Value(string.Join(zh ? "、" : ", ", fields.Skip(2)), zh);
            yield return zh ? $"{tag}：{pages} 上的 {left} 个功能" : $"{tag}: {left} functions on {pages}";
        }

        if (plan.Details.GetValueOrDefault("orphanedCount") is { } total && total != devices.Length.ToString(CultureInfo.InvariantCulture))
        {
            yield return zh ? $"……共 {total} 个设备" : $"… {total} devices in all";
        }
    }

    /// <summary>
    /// After the change list of a device renumbering, the devices EPLAN is to number (device.renumber.plan's
    /// <c>devices</c> details, at most 50, and <c>deviceCount</c>); nothing for other plans.
    /// </summary>
    private static IEnumerable<string> ToNumber(WritePlan plan, bool zh)
    {
        if (!plan.Changes.Any(c => c.Property == "device tags") || plan.Details?.GetValueOrDefault("devices") is not { Length: > 0 } lines)
        {
            yield break;
        }

        yield return string.Empty;
        yield return zh ? "要编号的设备：" : "Devices to number:";
        string[] devices = lines.Split('\n');
        foreach (string device in devices)
        {
            yield return Value(device, zh);
        }

        if (plan.Details.GetValueOrDefault("deviceCount") is { } total && total != devices.Length.ToString(CultureInfo.InvariantCulture))
        {
            yield return zh ? $"……共 {total} 个设备" : $"… {total} devices in all";
        }
    }

    /// <summary>After the pages of a page renumbering, how they are numbered (page.renumber.plan's <c>numbering</c>); else nothing.</summary>
    private static IEnumerable<string> Numbering(WritePlan plan, bool zh)
    {
        if (plan.Changes.Any(c => c.Property == "page number") && plan.Details?.GetValueOrDefault("numbering") is { Length: > 0 } numbering)
        {
            yield return string.Empty;
            yield return (zh ? "编号：" : "Numbering: ") + Value(numbering, zh);
        }
    }

    /// <summary>
    /// How the engineer takes the change back: Ctrl+Z on the write's undo step, except for language settings, which no
    /// undo step holds (eplan_language_set, tool spec 6; api-notes A28).
    /// </summary>
    private static string Undo(string tool, WritePlan plan, bool zh) => plan.Changes.Any(c => c.Property == "languages")
        ? zh ? "Ctrl+Z 不能撤销语言设置；要恢复，用原来的值再设置一次。" : "Ctrl+Z in EPLAN does not undo language settings; to restore them, set the old values again."
        : (zh ? $"批准后可在 EPLAN 中按 Ctrl+Z 撤销“AI: {tool}”这一步。" : $"After approval, Ctrl+Z in EPLAN undoes the step \"AI: {tool}\".")
            + (plan.Changes.Any(c => c.Kind == "language") ? zh ? "语言设置可能不随之恢复，备份中有原样的项目。" : " The language settings may not come back with it; the backup has the project as it was." : string.Empty);

    /// <summary>
    /// After the languages a removal deletes, what goes with them (eplan_language_remove, tool spec 6): every translation
    /// in them, in language.remove.plan's <c>textsAffected</c> texts, and maybe the displayed languages (api-notes A28).
    /// </summary>
    private static IEnumerable<string> Translations(WritePlan plan, bool zh)
    {
        if (plan.Changes.Any(c => c.Kind == "language" && c.Action == "delete"))
        {
            string texts = Value(plan.Details?.GetValueOrDefault("textsAffected") ?? "?", zh);
            yield return string.Empty;
            yield return zh
                ? $"项目文本中这些语言的全部译文都将删除（共 {texts} 个文本）；EPLAN 可能同时修改显示语言。"
                : $"All translations in these languages are deleted from the project's texts ({texts} texts); EPLAN may change the displayed languages too.";
        }
    }

    /// <summary>
    /// After a translation's scope, what it may do beyond the texts counted (eplan_project_translate, tool spec 5): whether
    /// existing translations are rewritten from the database is not verified, so the engineer is told they may be.
    /// </summary>
    private static IEnumerable<string> Translating(WritePlan plan, bool zh)
    {
        if (plan.Changes.Any(c => c.Property == "translations"))
        {
            yield return string.Empty;
            yield return zh
                ? "翻译库中有词条的文本补全项目的全部翻译语言；已有的译文也可能按翻译库改写，可能改动大量文本。"
                : "Texts the translation database has entries for are filled in for all the project's translation languages; existing translations may be rewritten from it too, so many texts may change.";
        }
    }

    private static string Title(WritePlan plan, bool zh) => (plan.MasterData, plan.ProjectPath.Length > 0) switch
    {
        (null, _) => zh ? "批准 AI 修改 EPLAN 项目？" : "Approve AI changes to the EPLAN project?",
        (_, true) => zh ? "批准 AI 修改 EPLAN 部件库和项目？" : "Approve AI changes to EPLAN's parts database and the project?",
        _ => zh ? "批准 AI 修改 EPLAN 部件库？" : "Approve AI changes to EPLAN's parts database?",
    };

    /// <summary>
    /// For a write to system master data (ADR-0019): which database, its type and path, and that every project using it
    /// is affected, as lines ahead of the project; else nothing.
    /// </summary>
    private static string MasterData(MasterDataTarget? target, bool zh)
    {
        if (target is null)
        {
            return string.Empty;
        }

        string type = target.Server ? "SQL Server" : zh ? "文件型" : "file database";
        string path = target.Path.Length == 0 || target.Path == target.Database ? string.Empty : (zh ? "，" : ", ") + Value(target.Path, zh);
        return zh
            ? $"部件库：{Value(target.Database, zh)}（{type}{path}）\n这个部件库被所有项目共用：修改影响使用它的所有项目。\n"
            : $"Parts database: {Value(target.Database, zh)} ({type}{path})\nEvery project that uses this parts database is affected.\n";
    }

    /// <summary>For a write to system master data: where the database is backed up before the change (ADR-0019); else nothing.</summary>
    private static string MasterDataBackup(string? folder, bool zh) => folder is null
        ? string.Empty
        : zh
            ? $"执行前会先把部件库备份到 {Value(folder, zh)}\\ 下的新文件夹。"
            : $"Before the change, the parts database is backed up to a new folder in {Value(folder, zh)}\\. ";

    /// <summary>No undo step holds a change to master data (S24: not even the parts database's own transactions); only the backup restores it.</summary>
    private static string MasterDataUndo(WritePlan plan, bool zh) => plan.ProjectPath.Length > 0
        ? zh
            ? "部件库中的修改撤不回：Ctrl+Z 只撤销项目中的修改；需要时按备份恢复部件库。"
            : "Changes to the parts database cannot be undone: Ctrl+Z undoes only the project's changes. If needed, restore the database from its backup."
        : zh
            ? "部件库中的修改撤不回，Ctrl+Z 也不能撤销；需要时按备份恢复。"
            : "Changes to the parts database cannot be undone, not even with Ctrl+Z. If needed, restore it from its backup.";

    /// <summary>For a Destructive write: where the whole project is copied before the change (ADR-0013); else nothing.</summary>
    private static string Backup(string? folder, bool zh) => folder is null
        ? string.Empty
        : zh
            ? $"执行前会先把整个项目备份到 {Value(folder, zh)}\\ 下的新文件夹。"
            : $"Before the change, the whole project is backed up to a new folder in {Value(folder, zh)}\\. ";

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
