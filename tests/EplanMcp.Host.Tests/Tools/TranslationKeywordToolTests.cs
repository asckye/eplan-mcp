using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// The Host half of eplan_translation_keyword; matching and sorting are the Bridge's
/// (TranslationKeywordSearchOperationTests).
/// </summary>
[Trait("Size", "Small")]
public sealed class TranslationKeywordToolTests
{
    private readonly FakeBridgeClient _bridge = new();
    private readonly List<TranslationKeywordItem> _keywords = [];

    public TranslationKeywordToolTests()
    {
        // Slices like the Bridge does, so cursors can be followed.
        _bridge.Handlers[OperationNames.TranslationKeywordSearch] = args =>
        {
            var search = (TranslationKeywordArgs)args!;
            return new TranslationKeywordData
            {
                Database = @"$(MD_TRANSLATE)\Translate.db",
                Languages = ["de_DE", "zh_CN"],
                Items = [.. _keywords.Skip(search.Offset).Take(search.Limit)],
                Total = _keywords.Count,
            };
        };
    }

    [Fact]
    public async Task TranslationKeyword_PassesTheTrimmedQueryTheModeAndThePageToTheBridge()
    {
        await Search("  Motor  ", exact: true, limit: 7);

        (string operation, object? sent) = Assert.Single(_bridge.Calls);
        var args = Assert.IsType<TranslationKeywordArgs>(sent);
        Assert.Equal(OperationNames.TranslationKeywordSearch, operation);
        Assert.Equal(("Motor", true, 0, 7), (args.Query, args.Exact, args.Offset, args.Limit));
    }

    [Fact]
    public async Task TranslationKeyword_ReturnsTheDatabaseTheLanguagesAndTheTranslations()
    {
        _keywords.Add(new TranslationKeywordItem
        {
            Source = "Motor",
            Translations = new(StringComparer.Ordinal) { ["zh_CN"] = "电动机", ["de_DE"] = "Motor" },
        });

        CallToolResult result = await Search("Motor");

        JsonElement data = Structured(result);
        Assert.Equal(@"$(MD_TRANSLATE)\Translate.db", data.GetProperty("database").GetString());
        Assert.Equal(["de_DE", "zh_CN"], data.GetProperty("languages").EnumerateArray().Select(l => l.GetString()));
        JsonElement keyword = Assert.Single(data.GetProperty("keywords").EnumerateArray());
        Assert.Equal("Motor", keyword.GetProperty("source").GetString());
        Assert.Equal("电动机", keyword.GetProperty("translations").GetProperty("zh_CN").GetString());
        Assert.Equal((1, JsonValueKind.Null, false), (data.GetProperty("total").GetInt32(), data.GetProperty("nextCursor").ValueKind, data.GetProperty("truncated").GetBoolean()));
        Assert.Equal("Found 1 keyword.", Text(result, 0));
        Assert.Equal(data.GetRawText(), Text(result, 1));
    }

    [Fact]
    public async Task TranslationKeyword_Pages_CoverEveryKeywordOnceAndTheSummarySaysThereIsMore()
    {
        _keywords.AddRange(Enumerable.Range(1, 5).Select(i => new TranslationKeywordItem { Source = $"K{i}" }));
        var seen = new List<string>();
        var summaries = new List<string>();
        string cursor = string.Empty;

        do
        {
            CallToolResult page = await Search("K", limit: 2, cursor: cursor);
            JsonElement data = Structured(page);
            seen.AddRange(data.GetProperty("keywords").EnumerateArray().Select(k => k.GetProperty("source").GetString()!));
            summaries.Add(Text(page, 0));
            cursor = data.GetProperty("nextCursor").GetString() ?? string.Empty;
        }
        while (cursor.Length > 0);

        Assert.Equal(["K1", "K2", "K3", "K4", "K5"], seen);
        Assert.Equal(["Found 2 of 5 keywords (more: cursor).", "Found 2 of 5 keywords (more: cursor).", "Found 1 of 5 keywords."], summaries);
    }

    [Fact]
    public async Task TranslationKeyword_ACursorOfOtherArguments_IsRefused()
    {
        _keywords.AddRange(Enumerable.Range(1, 5).Select(i => new TranslationKeywordItem { Source = $"K{i}" }));
        string cursor = Structured(await Search("K", limit: 2)).GetProperty("nextCursor").GetString()!;

        CallToolResult result = await Search("K", exact: true, limit: 2, cursor: cursor);

        Assert.Contains("'cursor'", Error(result).GetProperty("metadata").GetProperty("violations").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslationKeyword_NothingFound_IsASuccessWithNoKeywords()
    {
        CallToolResult result = await Search("cable");

        Assert.Equal(0, Structured(result).GetProperty("keywords").GetArrayLength());
        Assert.Equal("Found 0 keywords.", Text(result, 0));
    }

    [Theory]
    [InlineData("", 20, "'query' must have 1 to 200 characters")]
    [InlineData("Motor", 0, "'limit' must be between 1 and 100")]
    [InlineData("Motor", 101, "'limit' must be between 1 and 100")]
    public async Task TranslationKeyword_InvalidQueryOrLimit_IsReportedWithoutCallingEplan(string query, int limit, string expected)
    {
        CallToolResult result = await Search(query, limit: limit);

        Assert.Contains(expected, Error(result).GetProperty("metadata").GetProperty("violations").GetString()!, StringComparison.Ordinal);
        Assert.Equal(0, _bridge.Invocations);
    }

    private Task<CallToolResult> Search(string query, bool exact = false, int limit = TranslationKeywordTools.DefaultLimit, string cursor = "") =>
        TranslationKeywordTools.TranslationKeywordAsync(_bridge, query, exact, limit, cursor, TestContext.Current.CancellationToken);

    private static string Text(CallToolResult result, int index) => ((TextContentBlock)result.Content[index]).Text;

    private static JsonElement Structured(CallToolResult result)
    {
        ToolResultAssert.Succeeded(result);
        return result.StructuredContent!.Value;
    }

    private static JsonElement Error(CallToolResult result)
    {
        Assert.True(result.IsError);
        return JsonDocument.Parse(Text(result, 0)).RootElement.GetProperty("error");
    }
}
