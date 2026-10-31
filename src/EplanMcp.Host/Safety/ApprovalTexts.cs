using System.Globalization;
using System.Text.Json;
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

    private static readonly string[] _translationFileDetails = ["file", "bytes", "sha256"];
    private static readonly string[] _translationLanguageDetails = ["entries", "referenceLanguage", "languages"];

    /// <summary>The dialog for <paramref name="question"/>, without its binding and deadline.</summary>
    public static ApprovalAskArgs For(ApprovalQuestion question, CultureInfo culture)
    {
        bool zh = culture.TwoLetterISOLanguageName == "zh";
        if (question.Release is { } suspension)
        {
            return Release(suspension, zh);
        }

        if (question.PackInstall is { } install)
        {
            return PackInstall(question.Tool, install, zh);
        }

        if (question.PlanApproval is { } planApproval)
        {
            return planApproval.Dialog(culture);
        }

        WritePlan plan = question.Plan;
        string count = plan.Changes.Count.ToString(CultureInfo.InvariantCulture);
        string project = plan.ProjectPath.Length == 0
            ? string.Empty
            : zh ? $"项目：{Value(plan.Project, zh)}（{Value(plan.ProjectPath, zh)}）\n" : $"Project: {Value(plan.Project, zh)} ({Value(plan.ProjectPath, zh)})\n";
        return new ApprovalAskArgs
        {
            Title = plan.Changes.Any(c => c.Kind == "knowledge review") ? zh ? "确认知识审核决定？" : "Confirm knowledge review decision?" : Title(plan, zh),
            Summary = zh
                ? $"工具：{question.Tool}\n{MasterData(plan.MasterData, zh)}{project}将进行以下修改（共 {count} 处）："
                : $"Tool: {question.Tool}\n{MasterData(plan.MasterData, zh)}{project}The following changes will be made ({count}):",
            Changes = [.. plan.Changes.Select(c => Change(c, zh, plan.Details)), .. TrialParameters(plan), .. Orphaned(plan, zh), .. ToNumber(plan, zh), .. Numbering(plan, zh), .. Translations(plan, zh), .. Translating(plan, zh), .. PartLists(plan, zh), .. TranslationImport(plan, zh), .. ValueSet(plan, zh), .. CableGeneration(plan, zh)],
            Note = plan.MasterData?.Kind == MasterDataKinds.Translations
                ? zh ? "导入撤不回，执行前会备份翻译库。" : "An import cannot be undone, the translation database is backed up first."
                : Backup(question.BackupFolder, zh) + MasterDataBackup(question.MasterDataBackupFolder, zh)
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
        bool translations = suspension.Tool == "eplan_translation_import";
        string scope = suspension.Database is { } database
            ? (translations ? (zh ? "翻译库：" : "Translation database: ") : (zh ? "部件库：" : "Parts database: ")) + Value(database, zh)
            : suspension.Project is { } project
            ? (zh ? "项目：" : "Project: ") + Value(project, zh)
            : zh ? $"范围：这个 EPLAN 实例（进程 {pid}）中的所有项目" : $"Scope: every project in this EPLAN instance (process {pid})";
        bool parts = suspension.Database is not null;
        string where = translations ? (zh ? "翻译库" : "the translation database") : parts ? (zh ? "部件库" : "the parts database") : (zh ? "项目" : "the project");
        return new ApprovalAskArgs
        {
            Title = zh ? "上次 AI 修改的结果未知" : "The outcome of the last AI change is unknown",
            Summary = zh
                ? $"时间：{time}\n工具：{suspension.Tool}\n{scope}\n这次修改被中断，无法确定是否已经执行，{where}中可能只有其中一部分。AI 的写入因此暂停。计划中的修改（最多 {SuspensionStore.MaxChanges} 处）："
                : $"Time: {time}\nTool: {suspension.Tool}\n{scope}\nThis change was interrupted and whether it was made is unknown; {where} may contain part of it. AI writes are suspended. Planned changes (up to {SuspensionStore.MaxChanges}):",
            Changes = suspension.Changes.Count == 0
                ? [zh ? "（暂停记录中没有变更清单）" : "(the suspension record lists no changes)"]
                : [.. suspension.Changes.Select(c => Value(c, zh))],
            Note = translations
                ? zh
                    ? "请先在 EPLAN 中核实翻译库；需要时关闭 EPLAN，按执行前的备份恢复 Translate.db。核实之后再继续，否则请保持暂停。"
                    : "Check the translation database in EPLAN first; if needed, close EPLAN and restore Translate.db from the backup made before the import. Continue only after that; otherwise keep writes suspended."
                : parts
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

    /// <summary>
    /// The confirmation of installing someone else's engineering pack (ADR-0024 section 4): what it is, who the manifest
    /// says made it (unsigned, so unchecked), its EPLAN files, its requirements, and that it only holds data.
    /// </summary>
    private static ApprovalAskArgs PackInstall(string tool, Packs.PackInstallInfo install, bool zh)
    {
        string author = install.Author is { } a ? Value(a, zh) : zh ? "（清单中没有）" : "(not in the manifest)";
        string size = Exports.ByteSize.Format(install.Bytes);
        string capabilities = install.Capabilities.Count == 0 ? (zh ? "无" : "none") : Value(string.Join(", ", install.Capabilities), zh);
        int more = install.NativeFiles.Count - Packs.PackInstallInfo.MaxNamed;
        return new ApprovalAskArgs
        {
            Title = zh ? "安装别人的工程包？" : "Install someone else's engineering pack?",
            Summary = zh
                ? $"工具：{tool}\n文件：{Value(install.File, zh)}（{size}）\n工程包：{Value(install.Id, zh)} {Value(install.Version, zh)}\n作者（清单中写的，没有签名，未核实）：{author}\n摘要：{install.Digest}\n能力要求：{capabilities}\n引用包外的资源：{install.Referenced} 个\n其中的 EPLAN 文件（共 {install.NativeFiles.Count} 个）："
                : $"Tool: {tool}\nFile: {Value(install.File, zh)} ({size})\nPack: {Value(install.Id, zh)} {Value(install.Version, zh)}\nAuthor (as the manifest says; unsigned, not checked): {author}\nDigest: {install.Digest}\nRequires: {capabilities}\nResources referenced outside the pack: {install.Referenced}\nIts EPLAN files ({install.NativeFiles.Count}):",
            Changes = [.. install.NativeFiles.Take(Packs.PackInstallInfo.MaxNamed).Select(f => Value(f, zh)), .. more > 0 ? [zh ? $"……另有 {more} 个" : $"... and {more} more"] : Array.Empty<string>()],
            Note = zh
                ? "工程包只含数据，EplanMcp 从不执行其中的内容。安装后不可变，不会改变任何项目的锁定；EPLAN 只会在 scratch 副本中打开其中的项目。只安装你信任的来源。"
                : "Packs hold data only; EplanMcp never runs anything in them. The installed version never changes and no project's lock changes; EPLAN opens its projects only in scratch copies. Install only what you trust.",
            ApproveLabel = zh ? "安装" : "Install",
            RejectLabel = zh ? "不安装" : "Do not install",
            CountdownFormat = zh ? "{0} 后自动不安装" : "Not installed automatically in {0}",
        };
    }

    /// <summary>One change as one line, in the dialog's language; <paramref name="details"/> are the plan's.</summary>
    internal static string Change(PlannedChange change, bool zh, IReadOnlyDictionary<string, string>? details = null)
    {
        if (change.Kind == "unverified API") { return "unverified: " + change.Name + "; " + change.Property; }
        if (change.Kind == "action")
        {
            List<ActionParameter> parameters = JsonSerializer.Deserialize<List<ActionParameter>>(change.NewValue ?? "[]", HostJson.Options) ?? [];
            return $"Write {Value(change.Name, zh)}: " + string.Join("; ", parameters.Select(p => Value(p.Name, zh) + "=" + Value(p.Name is "PROJECTNAME" or "Project" ? Path.GetFileName(p.Value) : p.Value, zh)))
                + "; " + string.Join("; ", (details?.GetValueOrDefault("effects") ?? string.Empty).Split(';').Select(s => Value(s, zh)));
        }
        string kind = zh ? change.Kind switch { "page" => "页", "device" => "设备", "language" => "语言", _ => change.Kind } : change.Kind;
        string property = zh ? change.Property switch { "description" => "描述", "tag" => "标识", _ => change.Property ?? string.Empty } : change.Property ?? string.Empty;
        string name = Value(change.Name, zh);
        return change.Action switch
        {
            _ when change.Property == "list" => zh
                ? $"从 {Value(details?.GetValueOrDefault("file") ?? "?", zh)} 导入项目的 {Value(change.Kind, zh)} 清单（{Value(details?.GetValueOrDefault("bytes") ?? "?", zh)} 字节，SHA-256 {Value(details?.GetValueOrDefault("sha256") ?? "?", zh)}）。现有清单及部件引用可能改变；逐行效果无法预报。转换器：{Value(details?.GetValueOrDefault("converter") ?? "?", zh)}；现有计数：{Value(details?.GetValueOrDefault("before") ?? "?", zh)}。"
                : $"Import the {Value(change.Kind, zh)} list into this project from {Value(details?.GetValueOrDefault("file") ?? "?", zh)} ({Value(details?.GetValueOrDefault("bytes") ?? "?", zh)} bytes, SHA-256 {Value(details?.GetValueOrDefault("sha256") ?? "?", zh)}). Existing project list data and article references may change. Exact row-level effects are not known in advance. Converter: {Value(details?.GetValueOrDefault("converter") ?? "?", zh)}; before: {Value(details?.GetValueOrDefault("before") ?? "?", zh)}.",
            // part.import.plan: the parts a file adds to or updates in the parts database, counted (ADR-0019).
            "create" when change.Kind == "parts" => zh ? $"从 {name} 新增 {Value(change.NewValue ?? "?", zh)} 个部件" : $"Add {Value(change.NewValue ?? "?", zh)} new parts from {name}",
            _ when change.Kind == "parts" => zh ? $"从 {name} 更新部件库中已有的 {Value(change.NewValue ?? "?", zh)} 个部件" : $"Update {Value(change.NewValue ?? "?", zh)} parts the database has, from {name}",

            // part.sync.plan: the project changes too (S24 r15).
            _ when change.Property == "stored parts" => zh
                ? $"项目也会被修改：在项目 {name} 中存入 {Value(change.NewValue ?? "?", zh)} 个被引用的部件（现有 {Value(change.OldValue ?? "?", zh)} 个）"
                : $"The project changes too: store {Value(change.NewValue ?? "?", zh)} referenced parts in project {name} (now {Value(change.OldValue ?? "?", zh)})",

            // A macro's change names the page; what goes there is its value (macro.insert.plan).
            "create" when change.Kind == "macro" => zh ? $"在页 {name} 上插入宏：{Value(change.NewValue ?? string.Empty, zh)}" : $"Insert a macro on page {name}: {Value(change.NewValue ?? string.Empty, zh)}",
            // A device's change names the page too; the part, the tag and the point are its value (device.place.plan).
            "create" when change.Kind == "device" => zh ? $"在页 {name} 上放置设备：{Value(change.NewValue ?? string.Empty, zh)}" : $"Place a device on page {name}: {Value(change.NewValue ?? string.Empty, zh)}",
            // An import names the file, its size and SHA-256 (import.dxf.plan; M4c design 4.5).
            "create" when change.Kind == "drawing" => zh ? $"在页 {name} 上导入图形：{Value(change.NewValue ?? string.Empty, zh)}" : $"Import a drawing on page {name}: {Value(change.NewValue ?? string.Empty, zh)}",
            "create" when change.Kind == "PDF comments" => zh ? $"向项目 {name} 导入 PDF 便笺（其他批注忽略，新增数执行后才知道）：{Value(change.NewValue ?? string.Empty, zh)}" : $"Import PDF sticky notes into project {name} (other annotations ignored; new count unknown until import): {Value(change.NewValue ?? string.Empty, zh)}",
            "create" when change.Kind == "project" => zh ? $"新建项目 {name}：{Value(change.NewValue ?? string.Empty, zh)}" : $"Create project {name}: {Value(change.NewValue ?? string.Empty, zh)}",
            "create" => zh ? $"新建{kind} {name}" : $"Create {kind} {name}",
            "delete" => (zh ? $"删除{kind} {name}" : $"Delete {kind} {name}") + (change.OldValue is null ? string.Empty : zh ? $"（{Value(change.OldValue, zh)}）" : $" ({Value(change.OldValue, zh)})"),
            // Only EPLAN can tell which connections it adds or removes (eplan_connection_generate, tool spec 5).
            // Only EPLAN's numbering rules give the new tags (eplan_device_renumber, tool spec 5): the scope, worded by the Bridge.
            _ when change.Property == "device tags" => (zh ? "重新编号：" : "Renumber ") + Value(change.OldValue ?? string.Empty, zh),
            _ when change.Property == "page number" => zh ? $"重新编号{kind} {name}" : $"Renumber {kind} {name}",
            // The numbering tools of M4c (terminals, cables, wire numbers): the scope, worded by the Bridge.
            _ when change.Property == "numbering" => (zh ? "重新编号：" : "Renumber ") + Value(change.OldValue ?? string.Empty, zh),
            _ when change.Property == "PLC bus data" => PlcImport(name, details, zh),
            _ when change.Kind == "cables" && change.Property == "generation" => GenerateCables(zh, details),
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
            // EPLAN does not tell which value set is applied now (placeholder.apply.plan): only the new one; its values follow.
            _ when change.Property == "value set" => zh
                ? $"给占位符 {name} 应用值集 {Value(change.NewValue ?? "?", zh)}"
                : $"Apply value set {Value(change.NewValue ?? "?", zh)} to placeholder {name}",
            _ when change.Property == "languages" => zh
                ? $"修改项目 {name} 的语言：{Quoted(change.OldValue, zh)} → {Quoted(change.NewValue, zh)}"
                : $"Change the languages of project {name}: {Quoted(change.OldValue, zh)} → {Quoted(change.NewValue, zh)}",
            _ => zh
                ? $"修改{kind} {name} 的{property}：{Quoted(change.OldValue, zh)} → {Quoted(change.NewValue, zh)}"
                : $"Change {property} of {kind} {name}: {Quoted(change.OldValue, zh)} → {Quoted(change.NewValue, zh)}",
        };
    }

    private static string GenerateCables(bool zh, IReadOnlyDictionary<string, string>? details)
    {
        string creation = Value(details?.GetValueOrDefault("creationScheme") ?? "?", zh);
        string numbering = Value(details?.GetValueOrDefault("numberingScheme") ?? "?", zh);
        string selection = Value(details?.GetValueOrDefault("selectionScheme") ?? "?", zh);
        return zh
            ? $"在本项目中生成或更新自动电缆，创建方案 {creation}、编号方案 {numbering}、选型方案 {selection}。保留已有电缆名称；本操作不重新编号已有电缆。无法提前预测新增电缆及选型效果。执行前会先备份项目。提交后的撤销尚未验证。"
            : $"Generate or update automatic cables in this project using creation {creation}, numbering {numbering}, selection {selection}. Existing cable names will be preserved; existing cable renumbering is outside this operation. New cables and selection effects cannot be predicted in advance. A project backup will be made first. Undo after commit has not been verified.";
    }

    private static IEnumerable<string> CableGeneration(WritePlan plan, bool zh)
    {
        if (!plan.Changes.Any(c => c.Kind == "cables" && c.Property == "generation") || plan.Details is not { } details)
        {
            yield break;
        }

        yield return "KEEPOLDNAMES=1, ONLYAUTOCABLES=1, STARTVALUE=1, STEPVALUE=1";
        yield return (zh ? "现有计数：" : "Before: ") + Value(details.GetValueOrDefault("before") ?? "?", zh);
        using JsonDocument cables = JsonDocument.Parse(details.GetValueOrDefault("cables") ?? "[]");
        foreach (JsonElement cable in cables.RootElement.EnumerateArray().Take(50))
        {
            yield return Value(cable.GetProperty("name").GetString() ?? string.Empty, zh) + "; automatic=" + Value(cable.GetProperty("automatic").GetRawText(), zh);
        }
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
    private static string Undo(string tool, WritePlan plan, bool zh) => plan.Changes.Any(c => c.Kind == "knowledge review")
        ? zh ? "记录本地知识审核；提升将新建正式规则。接受候选不授予正式资格。EPLAN 的 Ctrl+Z 不能撤销。" : "Records a local knowledge review; promotion creates a new official Rule. Acceptance grants no official standing. Ctrl+Z in EPLAN cannot undo it."
        : plan.Changes.Any(c => c.Kind == "binding")
          ? zh ? "仅修改本地绑定；EPLAN 的 Ctrl+Z 不能撤销。" : "Changes only the local binding; Ctrl+Z in EPLAN cannot undo it."
          : tool is ActionRunTools.Tool or ApiTryTools.Tool
        ? zh ? "执行后只比较指纹，不逐项核对；提交后一步 Ctrl+Z 尚未验证。" : "Fingerprint readback only; changes are not checked item by item. Single-step Ctrl+Z after commit is unverified."
        : tool == ProjectCreateTools.CreateTool
        ? zh ? "只新建项目，不覆盖或复用已有目录。结束后关闭；没有 EPLAN 撤销步骤，Ctrl+Z 不能撤销。" : "Creates only a new project; never overwrites or reuses a destination. Closed afterwards; no EPLAN undo step or Ctrl+Z."
        : tool == PlcImportTools.Tool
        ? zh ? "提交后 Ctrl+Z 撤销尚未验证，需要时请工程师核实并使用项目备份。" : "Undo after commit has not been verified. Ask the engineer to check the project and use the backup if needed."
        : tool == ListImportTools.ImportTool
        ? zh ? "提交后的撤销尚未验证；必要时请工程师检查并使用备份。" : "Undo after commit has not been verified; ask the engineer to check the project and use the backup if needed."
        : plan.Changes.Any(c => c.Kind == "cables" && c.Property == "generation")
        ? zh ? "提交后的撤销尚未验证；请保留备份。" : "Undo after commit has not been verified; retain the backup."
        : plan.Changes.Any(c => c.Property == "languages")
        ? zh ? "Ctrl+Z 不能撤销语言设置；要恢复，用原来的值再设置一次。" : "Ctrl+Z in EPLAN does not undo language settings; to restore them, set the old values again."
        : (zh ? $"批准后可在 EPLAN 中按 Ctrl+Z 撤销“AI: {tool}”这一步。" : $"After approval, Ctrl+Z in EPLAN undoes the step \"AI: {tool}\".")
            + (plan.Changes.Any(c => c.Kind == "language") ? zh ? "语言设置可能不随之恢复，备份中有原样的项目。" : " The language settings may not come back with it; the backup has the project as it was." : string.Empty);

    /// <summary>
    /// After a value set's change, the value it gives each variable (placeholder.apply.plan's <c>values</c>: variable TAB
    /// text per line), so the engineer approves the values, not only a name; nothing for other plans.
    /// </summary>
    private static IEnumerable<string> ValueSet(WritePlan plan, bool zh)
    {
        if (!plan.Changes.Any(c => c.Property == "value set") || plan.Details?.GetValueOrDefault("values") is not { Length: > 0 } lines)
        {
            yield break;
        }

        yield return string.Empty;
        yield return zh ? "值集中各变量的值：" : "The value set's values:";
        foreach (string[] fields in lines.Split('\n').Select(line => line.Split('\t', 2)))
        {
            yield return Value(fields[0], zh) + " = " + Value(fields.Length > 1 ? fields[1] : string.Empty, zh);
        }
    }

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

    /// <summary>The most part numbers the dialog lists per kind (tool spec eplan_part_import, section 5).</summary>
    public const int MaxParts = 20;

    /// <summary>
    /// After the changes of a parts import (part.import.plan's details): the file with its size and SHA-256, the first
    /// <see cref="MaxParts"/> part numbers added and updated, how many stay as they are, and how parts were matched;
    /// nothing for other plans.
    /// </summary>
    private static IEnumerable<string> PartLists(WritePlan plan, bool zh)
    {
        if (plan.MasterData?.Kind != MasterDataKinds.Parts || plan.Changes.Count == 0 || plan.Details is not { } details)
        {
            yield break;
        }

        yield return string.Empty;
        if (details.GetValueOrDefault("file") is { } file)
        {
            yield return (zh ? "文件：" : "File: ") + Value(file, zh);
        }

        // part.sync.plan's "updated" are overwritten with the project's data, and "stored" go into the project.
        bool sync = details.ContainsKey("stored");
        foreach ((string key, string heading) in new[]
        {
            ("added", zh ? "新增的部件：" : "Parts added:"),
            ("updated", sync ? (zh ? "用项目数据覆盖的部件：" : "Parts overwritten with the project's data:") : (zh ? "更新的部件：" : "Parts updated:")),
            ("stored", zh ? "存入项目的部件：" : "Parts stored in the project:"),
        })
        {
            string[] numbers = (details.GetValueOrDefault(key) ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (numbers.Length == 0)
            {
                continue;
            }

            yield return heading;
            foreach (string number in numbers.Take(MaxParts))
            {
                yield return Value(number, zh);
            }

            string total = details.GetValueOrDefault(key + "Count") ?? "?";
            if (total != numbers.Take(MaxParts).Count().ToString(CultureInfo.InvariantCulture))
            {
                yield return zh ? $"……共 {Value(total, zh)} 个" : $"… {Value(total, zh)} in all";
            }
        }

        string skipped = Value(details.GetValueOrDefault("skippedCount") ?? "0", zh);
        string mode = Value(details.GetValueOrDefault("mode") ?? "?", zh);
        if (details.GetValueOrDefault("matchBy") is not { } given)
        {
            yield return zh ? $"部件库中不变的部件：{skipped} 个（方式 {mode}）" : $"Parts left as they are in the database: {skipped} (mode {mode})";
            yield break;
        }

        string matchBy = Value(given, zh);
        yield return zh ? $"不变的部件：{skipped} 个（方式 {mode}，按 {matchBy} 判断是否已有）" : $"Parts left as they are: {skipped} (mode {mode}, matched by {matchBy})";
    }

    private static IEnumerable<string> TranslationImport(WritePlan plan, bool zh)
    {
        if (plan.MasterData?.Kind != MasterDataKinds.Translations || plan.Details is not { } details)
        {
            yield break;
        }

        yield return (zh ? "文件、字节数、SHA-256：" : "File, bytes, SHA-256: ") + string.Join("; ", _translationFileDetails.Select(k => Value(details.GetValueOrDefault(k) ?? "?", zh)));
        yield return (zh ? "词条数、参考语言、其他语言：" : "Entries, reference language, other languages: ") + string.Join("; ", _translationLanguageDetails.Select(k => Value(details.GetValueOrDefault(k) ?? "?", zh)));
        yield return zh ? "只新增词条、补上空缺译文，已有译文不会被改写。" : "This import adds entries and fills empty translations; existing translations are never rewritten.";
        using JsonDocument preview = JsonDocument.Parse(details.GetValueOrDefault("preview") ?? "[]");
        foreach (JsonElement row in preview.RootElement.EnumerateArray().Take(10))
        {
            yield return string.Join("; ", row.EnumerateObject().Select(p =>
            {
                string text = p.Value.GetString() ?? string.Empty;
                return Value(p.Name, zh) + ": " + Value(text[..Math.Min(text.Length, 40)], zh);
            }));
        }
    }

    private static string Title(WritePlan plan, bool zh) => (plan.MasterData, plan.ProjectPath.Length > 0) switch
    {
        (null, _) => zh ? "批准 AI 修改 EPLAN 项目？" : "Approve AI changes to the EPLAN project?",
        ({ Kind: MasterDataKinds.Translations }, _) => zh ? "批准 AI 修改 EPLAN 翻译库？" : "Approve AI changes to EPLAN's translation database?",
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
        if (target.Kind == MasterDataKinds.Translations)
        {
            return zh
                ? $"翻译库：{Value(target.Database, zh)}（{type}{path}）\n修改影响使用这个库的所有项目。\n"
                : $"Translation database: {Value(target.Database, zh)} ({type}{path})\nEvery project that uses this translation database is affected.\n";
        }

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

    private static string PlcImport(string project, IReadOnlyDictionary<string, string>? details, bool zh)
    {
        string Field(string key) => Value(details?.GetValueOrDefault(key) ?? "?", zh);
        return (zh
            ? $"把 {Field("file")}（{Field("bytes")} 字节，SHA-256 {Field("sha256")}）以 {Field("converter")} 导入项目 {project}。这会导入新设备，不保证匹配更新。新增对象的全名在导入前已存在时会回滚。执行前会备份项目。提交后撤销尚未验证。"
            : $"Import PLC bus data from {Field("file")} ({Field("bytes")} bytes, SHA-256 {Field("sha256")}) into this project using {Field("converter")}. This imports new devices; matching updates are not guaranteed. New objects whose full names already existed in the project roll the import back. A project backup will be made first. Undo after commit has not been verified.")
            + (zh ? " 文件预检不完整；在事务内检查导入前已有的全名。" : " File preflight is incomplete; new object names will be checked against pre-import full names inside the transaction.")
            + (zh ? $"（已识别 {Field("sourceDeviceCount")} 个源设备；现有 {Field("functions")} 个功能、{Field("plcBoxes")} 个 PLC 盒、{Field("articleReferences")} 个部件引用）。"
                : $" ({Field("sourceDeviceCount")} source devices; {Field("functions")} functions, {Field("plcBoxes")} PLC boxes, {Field("articleReferences")} article references).");
    }

    private static IEnumerable<string> TrialParameters(WritePlan plan)
    {
        if (plan.Details?.GetValueOrDefault("rawParameters") is not { } raw) { yield break; }
        string escaped = JsonSerializer.Serialize(raw);
        for (int offset = 0; offset < escaped.Length; offset += 300)
        { yield return string.Concat("parameters (JSON text): ", escaped.AsSpan(offset, Math.Min(300, escaped.Length - offset))); }
    }
    private static string Quoted(string? value, bool zh) => value is null ? (zh ? "（未设置）" : "(not set)") : "'" + Value(value, zh) + "'";

    /// <summary>
    /// Project text on one line (control characters become spaces), without the bidirectional controls that make text
    /// read differently from what it contains (as <see cref="ResultShaper.CleanText"/>), shortened with its length named.
    /// </summary>
    internal static string Value(string text, bool zh)
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
