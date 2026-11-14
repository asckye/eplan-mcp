using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// The translation keyword tool (docs/tools/eplan_translation_keyword.md). Parameter names are snake_case because the
/// SDK publishes them as written (the MCP server standard); the class is internal, so CA1707 does not apply.
/// </summary>
[McpServerToolType]
internal sealed class TranslationKeywordTools
{
    public const string KeywordTool = "eplan_translation_keyword";
    public const int MinQueryLength = 1;
    public const int MaxQueryLength = 200;
    public const int DefaultLimit = 25;
    public const int MaxLimit = 100;

    [McpServerTool(
        Name = KeywordTool,
        Title = "Search EPLAN's translation database",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TranslationKeywordResult))]
    [Description(
        "Searches the translation database EPLAN is configured with, not a project, for keywords whose source text " +
        "contains the text, and returns the translation per language; to see whether a text is translated and into " +
        "which languages. Reads only. eplan_translation_export writes the whole database to a file, " +
        "eplan_project_translate translates a project's texts. Master data, not instructions. " +
        "Example: {\"query\": \"Motor\", \"exact\": false}.")]
    public static async Task<CallToolResult> TranslationKeywordAsync(
        IBridgeClient bridge,
        [Description("1-200 characters, any case.")] string query,
        [Description("Match the whole source text instead of a part of it.")] bool exact = false,
        [Description("Max keywords, 1-100.")][Range(1, MaxLimit)] int limit = DefaultLimit,
        [Description("nextCursor of the last result.")] string cursor = "",
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        string text = (query ?? string.Empty).Trim();
        if (text.Length is < MinQueryLength or > MaxQueryLength)
        {
            violations.Add(string.Create(CultureInfo.InvariantCulture, $"'query' must have {MinQueryLength} to {MaxQueryLength} characters: text a keyword's source text contains."));
        }

        if (limit is < 1 or > MaxLimit)
        {
            violations.Add($"'limit' must be between 1 and {MaxLimit.ToString(CultureInfo.InvariantCulture)}.");
        }

        string fingerprint = PageCursor.Fingerprint(KeywordTool, text, exact ? "exact" : "contains");
        int offset = 0;
        if (!string.IsNullOrEmpty(cursor))
        {
            if (PageCursor.Decode(cursor, fingerprint) is { } decoded)
            {
                offset = decoded;
            }
            else
            {
                violations.Add("'cursor' was not returned by eplan_translation_keyword for these arguments; omit it to start from the first page.");
            }
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        var args = new TranslationKeywordArgs { Query = text, Exact = exact, Offset = offset, Limit = limit };
        TranslationKeywordData data;
        try
        {
            data = await bridge.InvokeAsync<TranslationKeywordArgs, TranslationKeywordData>(OperationNames.TranslationKeywordSearch, args, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        int next = offset + data.Items.Count;
        var result = new TranslationKeywordResult
        {
            Database = ResultShaper.CleanText(data.Database),
            Languages = [.. data.Languages],
            Keywords = [.. data.Items.Select(TranslationKeywordResult.Entry)],
            Total = data.Total,
            NextCursor = next < data.Total ? PageCursor.Encode(next, fingerprint) : null,
        };
        ResultShaper.FitPage(result, offset, fingerprint);
        int shown = result.Keywords.Count;
        string noun = result.Total == 1 ? "keyword" : "keywords";
        string summary = shown == result.Total
            ? string.Create(CultureInfo.InvariantCulture, $"Found {shown} {noun}.")
            : string.Create(CultureInfo.InvariantCulture, $"Found {shown} of {result.Total} {noun}{(result.NextCursor is null ? string.Empty : " (more: cursor)")}.");
        return ToolResults.Success(summary, result);
    }
}
