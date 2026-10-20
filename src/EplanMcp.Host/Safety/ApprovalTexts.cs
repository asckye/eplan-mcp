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
        WritePlan plan = question.Plan;
        string count = plan.Changes.Count.ToString(CultureInfo.InvariantCulture);
        return new ApprovalAskArgs
        {
            Title = zh ? "批准 AI 修改 EPLAN 项目？" : "Approve AI changes to the EPLAN project?",
            Summary = zh
                ? $"工具：{question.Tool}\n项目：{Value(plan.Project, zh)}（{Value(plan.ProjectPath, zh)}）\n将进行以下修改（共 {count} 处）："
                : $"Tool: {question.Tool}\nProject: {Value(plan.Project, zh)} ({Value(plan.ProjectPath, zh)})\nThe following changes will be made ({count}):",
            Changes = [.. plan.Changes.Select(c => Change(c, zh))],
            Note = zh
                ? $"批准后可在 EPLAN 中按 Ctrl+Z 撤销“AI: {question.Tool}”这一步。"
                : $"After approval, Ctrl+Z in EPLAN undoes the step \"AI: {question.Tool}\".",
            ApproveLabel = zh ? "批准" : "Approve",
            RejectLabel = zh ? "拒绝" : "Reject",
            CountdownFormat = zh ? "{0} 后自动拒绝" : "Rejected automatically in {0}",
        };
    }

    /// <summary>One change as one line, in the dialog's language.</summary>
    internal static string Change(PlannedChange change, bool zh)
    {
        string kind = zh ? change.Kind switch { "page" => "页", "device" => "设备", _ => change.Kind } : change.Kind;
        string property = zh ? change.Property switch { "description" => "描述", "tag" => "标识", _ => change.Property ?? string.Empty } : change.Property ?? string.Empty;
        string name = Value(change.Name, zh);
        return change.Action switch
        {
            "create" => zh ? $"新建{kind} {name}" : $"Create {kind} {name}",
            _ => zh
                ? $"修改{kind} {name} 的{property}：{Quoted(change.OldValue, zh)} → {Quoted(change.NewValue, zh)}"
                : $"Change {property} of {kind} {name}: {Quoted(change.OldValue, zh)} → {Quoted(change.NewValue, zh)}",
        };
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
