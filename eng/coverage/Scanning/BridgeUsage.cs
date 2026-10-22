namespace EplanMcp.Coverage.Scanning;

/// <summary>
/// catalog/mapping/bridge-usage.json: the EPLAN API members (doc-comment IDs) the add-in uses, with their use sites.
/// </summary>
internal sealed class BridgeUsage
{
    public const string RelativePath = "catalog/mapping/bridge-usage.json";

    /// <summary>File version of the scanned Eplan.EplApi.DataModelu.dll, for example 2026.0.3.25421.</summary>
    public required string EplanApi { get; init; }

    public required SortedDictionary<string, List<UsageSite>> Usages { get; init; }
}
