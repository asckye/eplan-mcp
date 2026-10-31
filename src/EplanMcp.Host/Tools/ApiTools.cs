using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

[McpServerToolType]
internal sealed class ApiTools
{
    public const string SearchTool = "eplan_api_search";
    public const string GetTool = "eplan_api_get";

    [McpServerTool(Name = SearchTool, Title = "Search the offline EPLAN API catalog", ReadOnly = true,
        Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ApiSearchResult))]
    [Description("Discovers every catalogued operation overload group and property dictionary, including excluded APIs, without EPLAN. Filters intersect; names/text ignore case. verified means live_verified; implemented includes tested/verified. Summaries absent from the source are null. Example: {\"namespace\":\"Eplan.EplApi.HEServices\",\"member_name\":\"Translate\"}. Use eplan_api_get for signatures and evidence; catalog entries do not authorize execution.")]
    public static CallToolResult Search(ApiIndex index,
        [Description("Exact namespace including descendants.")][MinLength(1), MaxLength(500)] string? @namespace = null,
        [Description("Exact full declaring type, optionally prefixed T:.")][MinLength(1), MaxLength(500)] string? type = null,
        [Description("Member name substring.")][MinLength(1), MaxLength(500)] string? member_name = null,
        [Description("Documentation summary substring; unavailable summaries do not match.")][MinLength(1), MaxLength(500)] string? summary_text = null,
        [AllowedValues("method", "property", "operator", "delegate_method", "data_dictionary")] string? kind = null,
        [AllowedValues("implemented", "verified", "not_implemented", "excluded")] string? status = null,
        [Range(1, 100)] int limit = 20, string? cursor = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var violations = new List<string>();
        foreach (string? value in new[] { @namespace, type, member_name, summary_text })
        {
            if (value is not null && (string.IsNullOrWhiteSpace(value) || value.Length > 500))
            {
                violations.Add("Filters must contain 1 to 500 characters; omit unused filters.");
            }
        }
        if (kind is not null && kind is not ("method" or "property" or "operator" or "delegate_method" or "data_dictionary"))
        {
            violations.Add("Use a kind from the schema enum.");
        }
        if (status is not null && status is not ("implemented" or "verified" or "not_implemented" or "excluded"))
        {
            violations.Add("Use a status from the schema enum.");
        }
        ApiRow[] rows = [.. index.Search(@namespace, type, member_name, summary_text, kind, status).Select(e => e.Row)];
        string fingerprint = PageCursor.Fingerprint(SearchTool, index.Fingerprint, @namespace, type, member_name, summary_text, kind, status, limit.ToString(CultureInfo.InvariantCulture));
        return Page(rows, limit, cursor, fingerprint, SearchTool, violations,
            (items, next, more, hint) => new ApiSearchResult(index.Data.CatalogVersion, items, rows.Length, next, more, hint), cancellationToken);
    }

    [McpServerTool(Name = GetTool, Title = "Describe an offline EPLAN API group", ReadOnly = true,
        Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ApiGetResult))]
    [Description("Gets a catalogued API group by exact XML member ID, group key or dictionary type ID, without EPLAN. Pages full overload signatures then tool-level evidence; pass nextCursor as cursor for the rest. Null summaries were not stored in the source. Example: {\"id\":\"D:Function\"}. Discover IDs with eplan_api_search. Catalog status does not authorize execution.")]
    public static CallToolResult Get(ApiIndex index, [MinLength(1), MaxLength(4096)] string id, [Range(1, 100)] int limit = 20,
        string? cursor = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 4096)
        {
            violations.Add("id must contain 1 to 4096 characters; use an ID from eplan_api_search.");
        }
        ApiEntry? entry = index.Data.Entries.FirstOrDefault(e => e.Aliases.Contains(id, StringComparer.Ordinal));
        ApiDetail[] details = entry is null ? [] : [.. entry.Details, .. entry.Row.Tools
            .SelectMany(t => index.Data.Evidence.GetValueOrDefault(t) ?? []).Distinct()];
        string fingerprint = PageCursor.Fingerprint(GetTool, index.Fingerprint, id, limit.ToString(CultureInfo.InvariantCulture));
        return Page(details, limit, cursor, fingerprint, GetTool, violations,
            (items, next, more, hint) => new ApiGetResult(index.Data.CatalogVersion, entry!.Row, items, details.Length, next, more, hint), cancellationToken,
            entry is null ? new BridgeError { Reason = ErrorReason.ResourceNotFound, Message = "API ID not found. Use eplan_api_search and pass an exact id or group." } : null);
    }

    internal static CallToolResult Page<T, TResult>(T[] rows, int limit, string? cursor, string fingerprint, string tool,
        List<string> violations, Func<List<T>, string?, bool, string?, TResult> create, CancellationToken cancellationToken, BridgeError? missing = null)
    {
        if (limit is < 1 or > 100)
        {
            violations.Add("limit must be between 1 and 100.");
        }
        int offset = PageCursor.Offset(cursor is { Length: 0 or > 1024 } ? "invalid" : cursor, fingerprint, tool, violations);
        if (cursor is not null && offset >= rows.Length)
        {
            violations.Add("cursor is outside this result; omit it to restart.");
        }
        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }
        if (missing is not null)
        {
            return ToolResults.Error(missing);
        }
        var items = new List<T>();
        TResult Result() => create(items, offset + items.Count < rows.Length ? PageCursor.Encode(offset + items.Count, fingerprint) : null,
            offset + items.Count < rows.Length, offset + items.Count < rows.Length ? "Pass nextCursor as cursor to continue." : null);
        foreach (T row in rows.Skip(offset).Take(limit))
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(row);
            if (ResultShaper.ResultTokens(JsonSerializer.Serialize(Result(), HostJson.Options)) > (tool.EndsWith("_search", StringComparison.Ordinal) ? ResultShaper.DefaultTokenBudget : ResultShaper.MaxTokenBudget) - 100)
            {
                items.RemoveAt(items.Count - 1);
                break;
            }
        }
        return ToolResults.Success("Listed " + ToolResults.Count(items.Count, "catalog item") + ".", Result());
    }
}
