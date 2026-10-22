namespace EplanMcp.Coverage.Scanning;

/// <summary>One Bridge operation in catalog/mapping/bridge-usage.json; Kind is the enum value of a shared class (ExportKind.Pdf).</summary>
internal sealed record OperationEntry(string Name, string Class, string? Kind);

/// <summary>
/// catalog/mapping/bridge-usage.json: the EPLAN API members (doc-comment IDs) and actions (A: IDs) the add-in uses, with
/// their use sites and the Bridge operations that reach them.
/// </summary>
internal sealed class BridgeUsage
{
    public const string RelativePath = "catalog/mapping/bridge-usage.json";

    /// <summary>File version of the scanned Eplan.EplApi.DataModelu.dll, for example 2026.0.3.25421.</summary>
    public required string EplanApi { get; init; }

    public required IReadOnlyList<OperationEntry> Operations { get; init; }

    public required SortedDictionary<string, List<UsageSite>> Usages { get; init; }
}
