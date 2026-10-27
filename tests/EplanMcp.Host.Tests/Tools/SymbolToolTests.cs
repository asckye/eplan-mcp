using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class SymbolToolTests
{
    private readonly FakeBridgeClient _bridge = new();

    public SymbolToolTests() => _bridge.Handlers[OperationNames.SymbolSearch] = sent =>
    {
        var args = (SymbolSearchArgs)sent!;
        return new ListData<SymbolSearchItem> { Project = "Line1", Total = 3, Items = [.. Enumerable.Range(0, 3).Skip(args.Offset).Take(args.Limit).Select(i => new SymbolSearchItem { Library = "SPECIAL", Name = "CO\u202e", Variant = i, Type = "Routing", Kind = "routing", ConnectionPoints = [new() { Location = [-4, 0], Direction = "Left" }] })] };
    };

    [Fact]
    public async Task Search_SuccessRoundTripsWithAnOmittedErrorFlag()
    {
        CallToolResult result = await SymbolTools.SearchAsync(_bridge, "Line1", limit: 1, cancellationToken: TestContext.Current.CancellationToken);
        string json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        CallToolResult response = JsonSerializer.Deserialize<CallToolResult>(json, McpJsonUtilities.DefaultOptions)!;
        Assert.Null(response.IsError);
        Assert.True(response.IsError is not true);
        using JsonDocument wire = JsonDocument.Parse(json);
        Assert.False(wire.RootElement.TryGetProperty("isError", out _));
        Assert.True(JsonElement.DeepEquals(Data(result), Data(response)));
    }

    [Fact]
    public async Task Search_DefaultsAndFiltersReachTheTypedRead()
    {
        await SymbolTools.SearchAsync(_bridge, " Line1 ", " SPECIAL ", " CO ", "routing", cancellationToken: TestContext.Current.CancellationToken);
        var args = Assert.IsType<SymbolSearchArgs>(_bridge.LastArgs);
        Assert.Equal(("Line1", "SPECIAL", "CO", "routing", 50, 0), (args.Project, args.Library, args.NameContains, args.Kind, args.Limit, args.Offset));
        Assert.Equal(OperationNames.SymbolSearch, Assert.Single(_bridge.Calls).Operation);
    }

    [Fact]
    public async Task Search_PagesWithoutLossAndCleansProjectText()
    {
        var variants = new List<int>();
        string? cursor = null;
        do
        {
            JsonElement data = Data(await SymbolTools.SearchAsync(_bridge, "Line1", limit: 1, cursor: cursor, cancellationToken: TestContext.Current.CancellationToken));
            JsonElement item = Assert.Single(data.GetProperty("symbols").EnumerateArray());
            Assert.Equal("CO", item.GetProperty("name").GetString());
            Assert.Equal("Left", item.GetProperty("connectionPoints")[0].GetProperty("direction").GetString());
            variants.Add(item.GetProperty("variant").GetInt32());
            cursor = data.GetProperty("nextCursor").GetString();
            Assert.False(data.GetProperty("truncated").GetBoolean());
        } while (cursor is not null);
        Assert.Equal([0, 1, 2], variants);
    }

    [Theory]
    [InlineData("project")]
    [InlineData("library")]
    [InlineData("name")]
    [InlineData("kind")]
    public async Task Cursor_BindsEveryFilter(string changed)
    {
        string? cursor = Data(await SymbolTools.SearchAsync(_bridge, "Line1", "SPECIAL", "CO", "routing", 1, cancellationToken: TestContext.Current.CancellationToken)).GetProperty("nextCursor").GetString();
        CallToolResult result = await SymbolTools.SearchAsync(_bridge, changed == "project" ? "Line2" : "Line1", changed == "library" ? "IEC" : "SPECIAL", changed == "name" ? "K" : "CO", changed == "kind" ? "function" : "routing", 1, cursor, TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
        Assert.Contains("'cursor'", Text(result), StringComparison.Ordinal);
        Assert.Single(_bridge.Calls);
    }

    [Fact]
    public async Task InvalidArguments_AreAggregatedBeforeAnyBridgeCall()
    {
        CallToolResult result = await SymbolTools.SearchAsync(_bridge, " ", kind: "Routing", limit: 0, cursor: "bad", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
        Assert.All(["'project'", "'kind'", "'limit'", "'cursor'"], field => Assert.Contains(field, Text(result), StringComparison.Ordinal));
        Assert.Empty(_bridge.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task InvalidLimit_IsRejected(int limit) => Assert.True((await SymbolTools.SearchAsync(_bridge, "Line1", limit: limit, cancellationToken: TestContext.Current.CancellationToken)).IsError);

    [Fact]
    public async Task BridgeFailure_BecomesAToolError()
    {
        _bridge.Failure = new() { Reason = ErrorReason.ProjectNotOpen, Message = "Call eplan_project_list." };
        CallToolResult result = await SymbolTools.SearchAsync(_bridge, "missing", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
        Assert.Contains(ErrorReason.ProjectNotOpen, Text(result), StringComparison.Ordinal);
        Assert.Null(result.StructuredContent);
    }

    [Fact]
    public async Task LargePage_UsesExistingBudgetAndContinuation()
    {
        _bridge.Handlers[OperationNames.SymbolSearch] = _ => new ListData<SymbolSearchItem> { Project = "Line1", Total = 200, Items = [.. Enumerable.Range(0, 200).Select(i => new SymbolSearchItem { Name = new string('a', 500), Variant = i })] };
        CallToolResult result = await SymbolTools.SearchAsync(_bridge, "Line1", limit: 200, cancellationToken: TestContext.Current.CancellationToken);
        JsonElement data = Data(result);
        Assert.True(data.GetProperty("truncated").GetBoolean());
        Assert.InRange(data.GetProperty("symbols").GetArrayLength(), 1, 199);
        Assert.Equal(data.GetProperty("symbols").GetArrayLength(), PageCursor.Decode(data.GetProperty("nextCursor").GetString()!, PageCursor.Fingerprint(SymbolTools.Tool, "Line1", null, null, null)));
        Assert.InRange(ResultShaper.EstimateTokens(data.GetRawText()) * 2, 1, ResultShaper.DefaultTokenBudget);
    }

    private static JsonElement Data(CallToolResult result) => result.StructuredContent!.Value;
    private static string Text(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;
}
