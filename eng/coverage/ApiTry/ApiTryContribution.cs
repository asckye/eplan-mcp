namespace EplanMcp.Coverage;

internal enum ApiTryClaim { Own, Decorate }
internal sealed record ApiTrySelection(string Signature, ApiTryClaim Claim, string? ExclusionReason = null);
internal enum ApiTrySlot
{
    Catalog, Dispatch, Report, FileCatalog, SettingsCatalog, SettingsDispatch, SettingsReport,
    EntryReport, HandleCatalog, HandleDispatch, HandleReport, ChainCatalog, LifecycleReadback,
    ChainGaps, ChainGapsBefore, ChainReachability,
}
internal delegate string ApiTryFragment(ApiTryComposer composer, string original);

internal sealed class ApiTryContribution
{
    internal IReadOnlyList<ApiTrySelection> Members { get; init; } = [];
    internal Dictionary<ApiTrySlot, ApiTryFragment> DispatchFragments { get; init; } = [];
    internal Dictionary<ApiTrySlot, ApiTryFragment> TableRows { get; init; } = [];
    internal Dictionary<ApiTrySlot, ApiTryFragment> Templates { get; init; } = [];
    internal IEnumerable<KeyValuePair<ApiTrySlot, ApiTryFragment>> Outputs => DispatchFragments.Concat(TableRows).Concat(Templates);
}
