namespace EplanMcp.Host.Tools;

internal sealed record ApiIndexData(ApiEntry[] Entries, Dictionary<string, ApiDetail[]> Evidence, string CatalogVersion = "unknown");
internal sealed record ApiRow(string Id, string Group, string Kind, string Signature, string Status,
    string? ExcludedReason, string[] Tools, bool Partial);
internal sealed record ApiEntry(ApiRow Row, string Namespace, string Type, string Name, string? Summary,
    string[] Aliases, ApiDetail[] Details);
internal sealed record ApiParameter(string Name, string Type);
internal sealed record ApiDetail(string Kind, string Id, string? Signature = null, string? Summary = null,
    ApiParameter[]? Parameters = null, string? ReturnType = null, string? Accessors = null, bool Static = false,
    string? ExcludedReason = null, string? Reference = null, string? Note = null, int? PropertyCount = null, string? SetterAccess = null, string? Access = null);
internal sealed record ApiSearchResult(string CatalogVersion, IReadOnlyList<ApiRow> Items, int Total, string? NextCursor, bool Truncated, string? Hint);
internal sealed record ApiGetResult(string CatalogVersion, ApiRow Entry, IReadOnlyList<ApiDetail> Items, int Total, string? NextCursor, bool Truncated, string? Hint);
