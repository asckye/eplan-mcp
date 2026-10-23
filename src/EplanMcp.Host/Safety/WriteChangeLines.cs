using EplanMcp.Contracts;

namespace EplanMcp.Host.Safety;

/// <summary>The change list as one line per change, for the audit, the suspension and the approval form.</summary>
internal static class WriteChangeLines
{
    public static IEnumerable<string> For(WritePlan plan) => plan.Changes.Select(c => c.Action switch
    {
        "run" when c.Kind == "action" => ApprovalTexts.Change(c, zh: false, plan.Details),
        _ when c.Property == "list" => $"import {c.Kind} list into project {c.Name} from {plan.Details?.GetValueOrDefault("file")} ({plan.Details?.GetValueOrDefault("bytes")} bytes, SHA-256 {plan.Details?.GetValueOrDefault("sha256")}); converter {plan.Details?.GetValueOrDefault("converter")}; before {plan.Details?.GetValueOrDefault("before")}",
        // part.import.plan: the parts a file adds to or updates in the parts database, counted (ADR-0019).
        "create" when c.Kind == "parts" => $"add {Parts(c.NewValue)} from {c.Name} to the parts database",
        _ when c.Kind == "parts" => $"update {Parts(c.NewValue)} of the parts database from {c.Name}",

        // part.sync.plan: the referenced parts EPLAN stores in the project as well (S24 r15).
        _ when c.Property == "stored parts" => $"store {Parts(c.NewValue)} the project references in project {c.Name} (now {c.OldValue})",
        "create" => c.NewValue is null ? $"create {c.Kind} {c.Name}" : $"create {c.Kind} {c.Name} ({c.NewValue})",
        "delete" => c.OldValue is null ? $"delete {c.Kind} {c.Name}" : $"delete {c.Kind} {c.Name} ({c.OldValue})",
        _ when c.Property == "connections" => $"generate connections on {c.Kind} {c.Name} (now {c.OldValue}; may add or remove connections)",
        _ when c.Kind == "cables" && c.Property == "generation" => ApprovalTexts.Change(c, zh: false, plan.Details),
        _ when c.Property == "check messages" => CheckRunLine(plan, c),
        _ when c.Property == "reports" => $"update all reports ({c.OldValue} report pages now); may create, replace or delete report pages",
        _ when c.Property == "report" => $"update report page {c.Name}",
        _ when c.Property == "device tags" => $"renumber {c.OldValue}",
        _ when c.Property == "page number" => $"renumber {c.Kind} {c.Name}",
        _ when c.Property == "numbering" => $"renumber {c.OldValue}",
        _ when c.Property == "PLC bus data" => $"import PLC bus data: {c.OldValue}",
        _ when c.Property == "translations" => $"translate {c.OldValue}",
        _ when c.Property == "value set" => $"apply value set '{c.NewValue}' to placeholder {c.Name}",
        _ => $"update {c.Kind} {c.Name}: {c.Property} '{c.OldValue}' -> '{c.NewValue}'",
    });

    private static string Parts(string? count) => count == "1" ? "1 part" : (count ?? "?") + " parts";

    /// <summary>A check replaces all the project's check messages; check.run.plan puts its scope in the details.</summary>
    private static string CheckRunLine(WritePlan plan, PlannedChange change)
    {
        string scheme = plan.Details?.GetValueOrDefault("scheme") is { } given ? "scheme: " + given : "EPLAN's current scheme";
        return $"run check on {plan.Details?.GetValueOrDefault("pageCount") ?? "?"} pages ({scheme}), replacing the {change.OldValue} existing check messages";
    }
}
