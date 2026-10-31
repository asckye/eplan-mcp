using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class ApiToolTests
{
    private static ApiIndex Index => new(new([
        Entry("A", "method", "live_verified", "Find text", "N.T"),
        Entry("B", "property", "registered", null, "N.Child.T"),
        Entry("C", "operator", "excluded", "Find other", "Other.T"),
    ], new(StringComparer.Ordinal)), "digest");

    [Theory]
    [InlineData("implemented", "implemented", true)]
    [InlineData("automated_test", "implemented", true)]
    [InlineData("live_verified", "implemented", true)]
    [InlineData("automated_test", "verified", false)]
    [InlineData("live_verified", "verified", true)]
    [InlineData("registered", "not_implemented", true)]
    [InlineData("env_blocked", "not_implemented", true)]
    [InlineData("excluded", "not_implemented", false)]
    [InlineData("excluded", "excluded", true)]
    public void Status_MatchesDocumentedNestedLevels(string status, string filter, bool matches) =>
        Assert.Equal(matches, ApiIndex.MatchesStatus(status, filter));

    [Fact]
    public void Search_FiltersIntersectAndTextIgnoresCase()
    {
        Assert.Equal(2, Data(ApiTools.Search(Index, @namespace: "n", cancellationToken: TestContext.Current.CancellationToken)).GetProperty("total").GetInt32());
        Assert.Equal("O:A", Assert.Single(Data(ApiTools.Search(Index, @namespace: "N", type: "N.T",
            member_name: "a", summary_text: "TEXT", kind: "method", status: "verified", cancellationToken: TestContext.Current.CancellationToken)).GetProperty("items").EnumerateArray()).GetProperty("group").GetString());
        Assert.Equal(0, Data(ApiTools.Search(Index, @namespace: "N", type: "Other.T", cancellationToken: TestContext.Current.CancellationToken)).GetProperty("total").GetInt32());
        Assert.Equal(0, Data(ApiTools.Search(Index, type: "N.Child.T", summary_text: "missing", cancellationToken: TestContext.Current.CancellationToken)).GetProperty("total").GetInt32());
        Assert.Equal("O:A", Assert.Single(Data(ApiTools.Search(Index, type: "T:N.T", cancellationToken: TestContext.Current.CancellationToken)).GetProperty("items").EnumerateArray()).GetProperty("group").GetString());
    }

    [Fact]
    public void Paging_NoDuplicatesAndCursorBindsEveryFilterLimitToolAndDigest()
    {
        JsonElement first = Data(ApiTools.Search(Index, limit: 1, cancellationToken: TestContext.Current.CancellationToken));
        string cursor = first.GetProperty("nextCursor").GetString()!;
        Assert.Equal("O:B", Assert.Single(Data(ApiTools.Search(Index, limit: 1, cursor: cursor, cancellationToken: TestContext.Current.CancellationToken)).GetProperty("items").EnumerateArray()).GetProperty("group").GetString());
        AssertError(ApiTools.Search(Index, @namespace: "N", limit: 1, cursor: cursor, cancellationToken: TestContext.Current.CancellationToken));
        AssertError(ApiTools.Search(Index, type: "N.T", limit: 1, cursor: cursor, cancellationToken: TestContext.Current.CancellationToken));
        AssertError(ApiTools.Search(Index, member_name: "A", limit: 1, cursor: cursor, cancellationToken: TestContext.Current.CancellationToken));
        AssertError(ApiTools.Search(Index, summary_text: "text", limit: 1, cursor: cursor, cancellationToken: TestContext.Current.CancellationToken));
        AssertError(ApiTools.Search(Index, kind: "method", limit: 1, cursor: cursor, cancellationToken: TestContext.Current.CancellationToken));
        AssertError(ApiTools.Search(Index, status: "verified", limit: 1, cursor: cursor, cancellationToken: TestContext.Current.CancellationToken));
        AssertError(ApiTools.Search(Index, limit: 2, cursor: cursor, cancellationToken: TestContext.Current.CancellationToken));
        AssertError(ApiTools.Search(new(Index.Data, "changed"), limit: 1, cursor: cursor, cancellationToken: TestContext.Current.CancellationToken));
        AssertError(ApiTools.Get(Index, "O:A", limit: 1, cursor: cursor, cancellationToken: TestContext.Current.CancellationToken));
        AssertError(ApiTools.Search(Index, limit: 1, cursor: PageCursor.Encode(999, PageCursor.Fingerprint(ApiTools.SearchTool, "digest", null, null, null, null, null, null, "1")), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0, "bad")]
    [InlineData(101, "")]
    public void InvalidArguments_AreAggregated(int limit, string cursor)
    {
        JsonElement error = AssertError(ApiTools.Search(Index, @namespace: " ", type: new string('x', 501), kind: "bad", status: "bad", limit: limit, cursor: cursor, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Filters", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("limit", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("kind", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("status", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        AssertError(ApiTools.Get(Index, " ", limit: limit, cursor: cursor, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Get_ByMemberReturnsWholeGroupAndCanPageEvidenceWithoutOmission()
    {
        ApiIndex index = Index;
        ApiEntry entry = index.Data.Entries[0];
        index = new(new([entry with { Details = [.. entry.Details, new("overload", "M:A(System.Int32)")] }],
            new(StringComparer.Ordinal) { ["eplan_api_get"] = [new("evidence", "eplan_api_get", Reference: "docs/tools/eplan_api_get.md")] }), "digest");
        var ids = new List<string>();
        string? cursor = null;
        do
        {
            JsonElement data = Data(ApiTools.Get(index, "M:A", limit: 1, cursor: cursor, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal("O:A", data.GetProperty("entry").GetProperty("group").GetString());
            ids.Add(Assert.Single(data.GetProperty("items").EnumerateArray()).GetProperty("id").GetString()!);
            cursor = data.GetProperty("nextCursor").GetString();
        } while (cursor is not null);
        Assert.Equal(["M:A", "M:A(System.Int32)", "eplan_api_get"], ids);
        AssertError(ApiTools.Get(index, "missing", cancellationToken: TestContext.Current.CancellationToken), ErrorReason.ResourceNotFound);
        AssertError(ApiTools.Get(index, null!, cancellationToken: TestContext.Current.CancellationToken));
        AssertError(ApiTools.Get(index, "M:A", cursor: new string('x', 1025), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(0, Data(ApiTools.Search(index, member_name: "none", cancellationToken: TestContext.Current.CancellationToken)).GetProperty("total").GetInt32());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => ApiTools.Get(index, "M:A", cancellationToken: cancelled.Token));
    }

    private static ApiEntry Entry(string name, string kind, string status, string? summary, string type) => new(
        new("M:" + name, "O:" + name, kind, name + "()", status, status == "excluded" ? "internal" : null, ["eplan_api_get"], false),
        type[..type.LastIndexOf('.')], "T:" + type, name, summary, ["O:" + name, "M:" + name],
        [new("overload", "M:" + name, name + "()", summary, [], "System.Void")]);

    internal static JsonElement Data(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        JsonElement data = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(data.GetRawText(), Assert.IsType<TextContentBlock>(result.Content[1]).Text);
        Assert.False(data.TryGetProperty("execution", out _));
        return data;
    }

    internal static JsonElement AssertError(CallToolResult result, string reason = ErrorReason.InvalidArgument)
    {
        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        using JsonDocument document = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        JsonElement error = document.RootElement.GetProperty("error");
        Assert.Equal(reason, error.GetProperty("reason").GetString());
        return error.Clone();
    }
}
