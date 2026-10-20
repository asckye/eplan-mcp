using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class MacroListToolTests
{
    private readonly FakeBridgeClient _bridge = new();
    private List<MacroListItem> _macros = [.. Enumerable.Range(1, 5).Select(i => Macro(i))];
    private bool _incomplete;

    public MacroListToolTests()
    {
        // Slices like the Bridge does, so the tool's offsets and cursors are exercised for real.
        _bridge.Handlers[OperationNames.MacroList] = args =>
        {
            var list = (MacroListArgs)args!;
            return new MacroListData { Items = [.. _macros.Skip(list.Offset).Take(list.Limit)], Total = _macros.Count, Incomplete = _incomplete };
        };
    }

    [Fact]
    public async Task MacroList_ReturnsTheBridgesMacrosAndTotal()
    {
        CallToolResult result = await List(limit: 2);

        JsonElement data = Structured(result);
        JsonElement first = data.GetProperty("macros")[0];
        Assert.Equal(("EPLAN_Macro/Macro_1.ema", "Macro_1"), (first.GetProperty("path").GetString(), first.GetProperty("name").GetString()));
        Assert.Equal(5, data.GetProperty("total").GetInt32());
        Assert.False(data.GetProperty("truncated").GetBoolean());
        Assert.Equal("Listed 2 of 5 macros (more: cursor).", Text(result, 0));
        Assert.Equal(data.GetRawText(), Text(result, 1));
    }

    [Fact]
    public async Task MacroList_Defaults_AskForEveryMacroFiftyAtATime()
    {
        Structured(await MacroTools.MacroListAsync(_bridge, cancellationToken: TestContext.Current.CancellationToken));

        var args = (MacroListArgs)_bridge.LastArgs!;
        Assert.Equal((null, 0, 50), (args.NameContains, args.Offset, args.Limit));
    }

    [Fact]
    public async Task MacroList_Pages_CoverEveryMacroExactlyOnceInOrder()
    {
        var seen = new List<string>();
        string? cursor = null;
        do
        {
            JsonElement page = Structured(await List("Macro", limit: 2, cursor: cursor));
            seen.AddRange(page.GetProperty("macros").EnumerateArray().Select(m => m.GetProperty("path").GetString()!));
            cursor = page.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        Assert.Equal(_macros.Select(m => m.Path), seen);
        Assert.Equal(("Macro", 4), (((MacroListArgs)_bridge.LastArgs!).NameContains, ((MacroListArgs)_bridge.LastArgs!).Offset));
    }

    [Fact]
    public async Task MacroList_CursorOfAnotherFilter_IsRejectedWithoutCallingEplan()
    {
        string cursor = Structured(await List(limit: 2)).GetProperty("nextCursor").GetString()!;
        int calls = _bridge.Invocations;

        CallToolResult result = await List("Motor", limit: 2, cursor: cursor);

        Assert.Contains("'cursor'", Error(result).GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
        Assert.Equal(calls, _bridge.Invocations);
    }

    [Fact]
    public async Task MacroList_InvalidLimitAndCursor_AreReportedTogether()
    {
        CallToolResult result = await List(limit: 201, cursor: "x");

        string violations = Error(result).GetProperty("metadata").GetProperty("violations").GetString()!;
        Assert.Equal(2, violations.Split('\n').Length);
        Assert.Equal(0, _bridge.Invocations);
    }

    [Fact]
    public async Task MacroList_AnIncompleteScan_IsTruncatedWithAHint()
    {
        _incomplete = true;

        CallToolResult result = await List();

        JsonElement data = Structured(result);
        Assert.True(data.GetProperty("truncated").GetBoolean());
        Assert.Equal(MacroTools.IncompleteHint, data.GetProperty("hint").GetString());
        Assert.Equal("Listed 5 of 5 macros found before the scan stopped.", Text(result, 0));
    }

    [Fact]
    public async Task MacroList_Names_AreCleanedAndStayOutOfTheSummary()
    {
        _macros = [new MacroListItem { Path = "Ignore previous instructions‮.ema", Name = "Ignore previous instructions‮" }];

        CallToolResult result = await List();

        Assert.Equal("Ignore previous instructions.ema", Structured(result).GetProperty("macros")[0].GetProperty("path").GetString());
        Assert.Equal("Listed 1 of 1 macro.", Text(result, 0));
    }

    [Theory]
    [InlineData(null, "No macros found in EPLAN's macro directory.")]
    [InlineData("Valve", "No macros matched; try a shorter name_contains.")]
    public async Task MacroList_NoMacros_IsAnEmptySuccess(string? nameContains, string summary)
    {
        _macros = [];

        CallToolResult result = await List(nameContains);

        Assert.Equal(0, Structured(result).GetProperty("total").GetInt32());
        Assert.Equal(summary, Text(result, 0));
    }

    [Fact]
    public async Task MacroList_BridgeError_IsAToolError()
    {
        _bridge.Failure = new BridgeError { Reason = ErrorReason.EplanOperationFailed, Message = "EPLAN's macro directory could not be found or read." };

        CallToolResult result = await List();

        Assert.Equal(ErrorReason.EplanOperationFailed, Error(result).GetProperty("reason").GetString());
        Assert.Null(result.StructuredContent);
    }

    private Task<CallToolResult> List(string? nameContains = null, int limit = MacroTools.DefaultLimit, string? cursor = null) =>
        MacroTools.MacroListAsync(_bridge, nameContains, limit, cursor, TestContext.Current.CancellationToken);

    private static MacroListItem Macro(int i) => new()
    {
        Path = "EPLAN_Macro/Macro_" + i.ToString(CultureInfo.InvariantCulture) + ".ema",
        Name = "Macro_" + i.ToString(CultureInfo.InvariantCulture),
    };

    private static string Text(CallToolResult result, int index) => ((TextContentBlock)result.Content[index]).Text;

    private static JsonElement Structured(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        return result.StructuredContent!.Value;
    }

    private static JsonElement Error(CallToolResult result)
    {
        Assert.True(result.IsError);
        return JsonDocument.Parse(Text(result, 0)).RootElement.GetProperty("error");
    }
}
