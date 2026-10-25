using System.Globalization;
using System.Text;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Plans;

/// <summary>Complete deterministic text, grouped by original step number and affected page.</summary>
internal static class PlanDiffPreview
{
    public static string For(IEnumerable<PlanStep> steps, bool local = false)
    {
        var lines = new List<string>();
        if (local)
        {
            lines.Add("Verification: local standard content plus project-wide name screening; other content is unobserved.");
        }
        foreach (PlanStep step in steps.OrderBy(s => s.Index))
        {
            lines.Add($"Step {step.Index.ToString(CultureInfo.InvariantCulture)}: {step.Op} [{step.Tier.ToString().ToLowerInvariant()}]");
            if (step.Prediction is not null)
            {
                lines.Add("  Prediction: " + Escape(JsonSerializer.Serialize(step.Expected, PlanJson.Options)));
            }
            foreach (var detail in step.Details.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                lines.Add("  " + detail.Key + ": " + Escape(detail.Value));
            }
            foreach (string page in step.ModifyScope.Pages.Concat(local ? step.ModifyScope.Locators.Select(l => l.Page).OfType<string>() : [])
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                lines.Add("  Page " + Escape(page));
                var changes = step.PlannedChanges.Where(c => c.Kind is not ("page" or "macro") || c.Name == page)
                    .Select(c => new PlannedChange { Action = c.Action, Kind = c.Kind, Name = c.Name, Property = c.Property, OldValue = c.OldValue, NewValue = c.NewValue }).ToList();
                foreach (JsonElement orphan in step.Expected.Where(e => e.GetProperty("tag").GetString() == "orphaned"
                    && e.GetProperty("value").GetProperty("remaining").EnumerateArray().Any(l => l.GetProperty("page").GetString() == page)))
                {
                    changes.Add(new() { Action = "update", Kind = "device", Name = orphan.GetProperty("target").GetProperty("name").GetString()!, Property = "main function", OldValue = "present", NewValue = "absent" });
                }
                lines.AddRange(WriteChangeLines.For(new() { Changes = changes, Details = step.Details.ToDictionary() }).Select(l => "    " + Escape(l)));
                if (!step.Effective)
                {
                    lines.Add("    No changes.");
                }
                else if (changes.Count == 0)
                {
                    lines.Add("    Impact scope; no direct change listed.");
                }
            }
        }
        return string.Join('\n', lines) + "\n";
    }

    internal static string Escape(string text)
    {
        var result = new StringBuilder();
        foreach (char c in text)
        {
            if (char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.Format
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                || c != ' ' && CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator)
            {
                result.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            }
            else
            {
                result.Append(c);
            }
        }
        return result.ToString();
    }
}
