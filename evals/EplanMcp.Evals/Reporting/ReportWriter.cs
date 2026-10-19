using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace EplanMcp.Evals.Reporting;

/// <summary>The figures of one task set; the medians cover the attempts that count, null when there are none.</summary>
internal sealed record SetSummary(
    string Set,
    int Tasks,
    int Counted,
    int Solved,
    double? SuccessRate,
    double? MedianToolCalls,
    double? MedianInputTokens,
    double? MedianOutputTokens,
    double? MedianSeconds,
    decimal CostUsd);

/// <summary>Writes <c>report.json</c> and the Chinese summary <c>report.md</c> of a run (agent evals design 4.3).</summary>
internal static class ReportWriter
{
    public const string JsonFile = "report.json";
    public const string MarkdownFile = "report.md";

    private static readonly string[] _sets = ["dev", "holdout"];

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task WriteAsync(RunReport report, string directory, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(directory, JsonFile), Json(report), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(directory, MarkdownFile), Markdown(report), cancellationToken).ConfigureAwait(false);
    }

    public static string Json(RunReport report) => JsonSerializer.Serialize(report, _json);

    /// <summary>One line per task set that has tasks; invalid attempts are left out of every figure but the cost.</summary>
    public static IReadOnlyList<SetSummary> Summarize(RunReport report) =>
    [
        .. _sets.Select(set => report.Tasks.Where(t => t.Set == set).ToList()).Where(tasks => tasks.Count > 0).Select(tasks =>
        {
            List<AttemptRecord> all = [.. tasks.SelectMany(t => t.Attempts)];
            List<AttemptRecord> counted = [.. all.Where(a => a.Outcome != Outcome.Invalid)];
            int solved = counted.Count(a => a.Outcome == Outcome.Solved);
            return new SetSummary(
                tasks[0].Set,
                tasks.Count,
                counted.Count,
                solved,
                counted.Count == 0 ? null : (double)solved / counted.Count,
                Median(counted.Select(a => (double)a.ToolCalls.Values.Sum())),
                Median(counted.Select(a => (double)a.InputTokens)),
                Median(counted.Select(a => (double)a.OutputTokens)),
                Median(counted.Select(a => a.Seconds)),
                all.Sum(a => a.CostUsd));
        }),
    ];

    public static string Markdown(RunReport report)
    {
        var text = new StringBuilder();
        Line(text, "# Agent 评测报告");
        Line(text);
        Line(text, $"- 开始时间：{report.Started:yyyy-MM-dd HH:mm:ss zzz}");
        Line(text, $"- 模型：`{report.Model}`");
        Line(text, $"- claude：`{report.Claude}`");
        Line(text, $"- eplanmcp：`{report.Eplanmcp}`");
        Line(text);
        Line(text, "## 汇总");
        Line(text, "| 集合 | 任务 | 计入的尝试 | 成功 | 成功率 | 调用次数中位数 | 输入 token 中位数 | 输出 token 中位数 | 耗时中位数（秒） | 费用（美元） |");
        Line(text, "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (SetSummary set in Summarize(report))
        {
            Line(text, $"| {(set.Set == "dev" ? "开发集" : "保留集")} | {set.Tasks} | {set.Counted} | {set.Solved} | {Percent(set.SuccessRate)} | {Figure(set.MedianToolCalls)} | {Figure(set.MedianInputTokens)} | {Figure(set.MedianOutputTokens)} | {Figure(set.MedianSeconds)} | {set.CostUsd.ToString("0.00", CultureInfo.InvariantCulture)} |");
        }

        Section(text, "失败", report.Tasks.SelectMany(t => t.Attempts.Where(a => a.Outcome == Outcome.Failed).SelectMany(a =>
            (IEnumerable<string>)[$"- `{t.Id}` 第 {a.Attempt} 次：{Inline(a.Reason)}", .. a.Failures.Select(f => $"  - {Inline(f)}")])));
        Section(text, "无效（不计入成功率）", report.Tasks.SelectMany(t => t.Invalid is { } invalid
            ? [$"- `{t.Id}`：{Inline(invalid)}"]
            : t.Attempts.Where(a => a.Outcome == Outcome.Invalid).Select(a => $"- `{t.Id}` 第 {a.Attempt} 次：{Inline(a.Reason)}")));
        Section(text, "无效的任务文件", report.InvalidFiles.Select(f => $"- {Inline(f)}"));
        Section(text, "提示", report.Tasks.SelectMany(t => t.Notes.Select(n => $"- `{t.Id}`：{Inline(n)}")));
        return text.ToString();
    }

    private static void Section(StringBuilder text, string title, IEnumerable<string> lines)
    {
        List<string> items = [.. lines];
        if (items.Count == 0)
        {
            return;
        }

        Line(text);
        Line(text, "## " + title);
        items.ForEach(item => Line(text, item));
    }

    private static void Line(StringBuilder text, string line = "") => text.Append(line).Append('\n');

    /// <summary>Keeps a reason on its list item: a reason quoting stderr can span lines.</summary>
    private static string Inline(string? text) => (text ?? string.Empty).ReplaceLineEndings(" ").Trim();

    private static double? Median(IEnumerable<double> values)
    {
        double[] sorted = [.. values.Order()];
        return sorted.Length == 0 ? null : sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2;
    }

    private static string Percent(double? rate) => rate is { } r ? (r * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "—";

    private static string Figure(double? value) => value is { } v ? v.ToString("0.#", CultureInfo.InvariantCulture) : "—";
}
