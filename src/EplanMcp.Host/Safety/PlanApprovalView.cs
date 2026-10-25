using System.Globalization;
using System.Text;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;

namespace EplanMcp.Host.Safety;

/// <summary>Host-only plan review, carried by the existing text fields of both approval channels.</summary>
internal sealed record PlanApprovalView(PlanRecord Record, string ChangesPath, string BackupFolder)
{
    internal const int MaxLines = 200;
    internal const int MaxLineLength = 400;
    public ToolTier Tier => Record.Definition.Steps.Max(s => s.Tier);

    public ApprovalQuestion Question(IReadOnlyList<string> independentFingerprints, EplanInstanceInfo eplan, DateTimeOffset deadline)
    {
        string planHash = Record.PlanHash;
        string planId = Record.Definition.PlanId;
        string argumentsHash = Hash(new
        {
            planId,
            planHash,
        });
        string fingerprint = Hash(new
        {
            planHash,
            independentFingerprints,
        });
        return new ApprovalQuestion(Guid.NewGuid().ToString("N"), "eplan_plan_apply", argumentsHash,
            new WritePlan
            {
                Project = Record.Definition.Project.Name,
                ProjectPath = Record.Definition.Project.Path,
                Fingerprint = fingerprint,
            }, [], deadline, BackupFolder: BackupFolder)
        {
            Tier = Tier,
            Eplan = eplan,
            PlanApproval = this,
        };
    }

    public List<string> WorkbenchChanges(CultureInfo culture)
    {
        bool zh = culture.TwoLetterISOLanguageName == "zh";
        List<string> header = Header(zh);
        string[] full = [.. FullLines().SelectMany(line => Chunks(line))];
        if (header.Count + full.Length <= MaxLines)
        {
            return [.. header, .. full];
        }

        // Reserve every step's overview before spending the remaining space on the complete grouped list.
        string[] summaries = [.. Summaries()];
        int remaining = MaxLines - header.Count - summaries.Length - 1;
        if (remaining < 0)
        {
            throw Invalid("The plan approval metadata cannot fit in the review text.");
        }

        return [.. header, Marker(zh), .. summaries, .. full.Take(remaining)];
    }

    public ApprovalAskArgs Dialog(CultureInfo culture)
    {
        bool zh = culture.TwoLetterISOLanguageName == "zh";
        string[] changes = [.. FullLines().Where(l => l.StartsWith("    ", StringComparison.Ordinal))];
        return new ApprovalAskArgs
        {
            Title = zh ? "批准 AI 计划？" : "Approve the AI plan?",
            Summary = string.Join('\n', Header(zh)),
            Changes = [Marker(zh), .. Summaries(), .. changes.Take(50).Select(Short)],
            Note = Limits(zh),
            ApproveLabel = zh ? "批准" : "Approve",
            RejectLabel = zh ? "拒绝" : "Reject",
            CountdownFormat = zh ? "{0} 后自动拒绝" : "Rejected automatically in {0}",
        };
    }

    private List<string> Header(bool zh)
    {
        if (!Path.IsPathFullyQualified(ChangesPath) || ChangesPath.Length > MaxLineLength || PlanDiffPreview.Escape(ChangesPath) != ChangesPath)
        {
            throw Invalid("The complete changes file path must be displayable on one line of at most 400 characters.");
        }

        PlanDefinition definition = Record.Definition;
        return
        [
            (zh ? "计划 ID：" : "Plan ID: ") + definition.PlanId,
            "planHash: " + Record.PlanHash,
            .. Chunks((zh ? "项目：" : "Project: ") + PlanDiffPreview.Escape(definition.Project.Name)),
            .. Chunks((zh ? "柜：" : "Cabinet: ") + PlanDiffPreview.Escape(definition.Cabinet)),
            (zh ? "最高级别：" : "Highest tier: ") + Tier,
            .. Chunks((zh ? "备份位置：" : "Backup location: ") + PlanDiffPreview.Escape(BackupFolder)),
            .. Chunks(Limits(zh)),
            .. definition.CompilerVersion == "2"
                ? [zh ? "校验：局部 standard 内容与全项目名称筛查；其他内容未观察。" : "Verification: local standard content plus project-wide name screening; other content is unobserved."]
                : Array.Empty<string>(),
            .. definition.Steps.Any(s => s.Prediction is not null)
                ? [zh ? "预测步骤按批准模式匹配；完整模式见清单。" : "Predicted steps match approved patterns; see the list for the complete patterns."]
                : Array.Empty<string>(),
            zh ? "执行结果请用 eplan_plan_get 查询。" : "Query execution results with eplan_plan_get.",
            zh ? "完整变更清单（纯文本路径）：" : "Complete changes list (plain text path):",
            ChangesPath,
            "SHA-256: " + definition.Digests.ChangesDigest,
        ];
    }

    // S35 / api-notes A41 verify the existing operations, not arbitrary combined module/value-set operations.
    private static string Limits(bool zh) => zh
        ? "各有效步有独立事务和 EPLAN 撤销步骤；撤销以已验证操作为限。撤销只作用于 EPLAN 当前项目，撤销前请先将目标设为当前项目。第一步前会备份；必要时使用备份。不会自动撤销已提交步骤。"
        : "Each effective step has its own transaction and EPLAN undo step; undo is limited to verified operations. Undo applies only to EPLAN's current project; make the target current before undoing. A backup is made before the first step; use it if needed. Committed steps are not automatically undone.";

    private static string Marker(bool zh) => zh ? "摘要，完整清单在文件。" : "Summary — full list in the file.";

    private IEnumerable<string> Summaries()
    {
        foreach (PlanStep step in Record.Definition.Steps.OrderBy(s => s.Index))
        {
            string[] pages = [.. step.ModifyScope.Pages.Concat(step.ModifyScope.Locators.Select(l => l.Page).OfType<string>())
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
            yield return Short(FormattableString.Invariant($"Step {step.Index}: {step.Op} [{step.Tier}] — {pages.Length} pages, {step.PlannedChanges.Length} changes; effective={step.Effective}"));
            yield return Short("  Pages: " + string.Join(", ", pages.Select(PlanDiffPreview.Escape)));
            if (step.Prediction is not null)
            {
                yield return Short("Prediction: match approved patterns: " + PlanDiffPreview.Escape(JsonSerializer.Serialize(step.Expected, PlanJson.Options)));
            }
        }
    }

    private string[] FullLines() => PlanDiffPreview.For(Record.Definition.Steps, Record.Definition.CompilerVersion == "2").TrimEnd('\n').Split('\n');

    private static string Short(string line)
    {
        string first = Chunks(line).First();
        return first.Length == line.Length ? first : Chunks(line, MaxLineLength - 1).First() + "…";
    }

    private static IEnumerable<string> Chunks(string line, int width = MaxLineLength)
    {
        do
        {
            int length = Math.Min(width, line.Length);
            if (length < line.Length && char.IsHighSurrogate(line[length - 1]))
            {
                length--;
            }

            yield return line[..length];
            line = line[length..];
        }
        while (line.Length > 0);
    }

    private static string Hash(object value) => PlanStore.Hash(Encoding.UTF8.GetBytes(PlanCanonical.Serialize(JsonSerializer.SerializeToElement(value))));

    internal static BridgeCallException Invalid(string message) => new(new BridgeError
    {
        Reason = ErrorReason.InvalidArgument,
        Message = message,
        Execution = ExecutionState.NotStarted,
    });
}
