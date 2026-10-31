using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class SelectionToolTests
{
    private readonly FakeBridgeClient _bridge = new();
    private static ObjectSummary Row(long oid) => new()
    {
        Name = "Name\u202e",
        Locator = new()
        { Project = @"D:\scratch\copy", Page = "=A/1", Type = 17, Oid = oid, Digest = new string('a', 64) }
    };
    private bool _changed;
    public SelectionToolTests()
    {
        _bridge.Handlers[OperationNames.SelectionGet] = value => new SelectionData
        {
            Project = @"D:\scratch\copy",
            Page = "=A/1",
            Total = 3,
            Fingerprint = _changed ? "changed" : "original",
            Objects = Enumerable.Range(((SelectionArgs)value!).Offset + 1, 3 - ((SelectionArgs)value!).Offset).Select(i => Row(i)).ToList()
        };
        _bridge.Handlers[OperationNames.SelectionSet] = value => new SelectionData
        {
            Project = @"D:\scratch\copy",
            Page = "=A/1",
            Total = 1,
            Objects = [Row(1)],
            Unselected = ((SelectionArgs)value!).Locators.Skip(1).ToList(),
            Exact = false
        };
    }
    private static JsonElement Data(ModelContextProtocol.Protocol.CallToolResult result) => ApiToolTests.Data(result);

    [Fact]
    public async Task Get_PagesAndBindsCursorToFormatLimitAndContents()
    {
        JsonElement first = Data(await SelectionTools.GetAsync(_bridge, 1, cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(first.GetProperty("items")[0].TryGetProperty("name", out _));
        string cursor = first.GetProperty("cursor").GetString()!;
        JsonElement second = Data(await SelectionTools.GetAsync(_bridge, 1, cursor, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(2, second.GetProperty("items")[0].GetProperty("locator").GetProperty("oid").GetInt32());
        ApiToolTests.AssertError(await SelectionTools.GetAsync(_bridge, 2, cursor, cancellationToken: TestContext.Current.CancellationToken));
        ApiToolTests.AssertError(await SelectionTools.GetAsync(_bridge, 1, cursor, "detailed", TestContext.Current.CancellationToken));
        _changed = true;
        ApiToolTests.AssertError(await SelectionTools.GetAsync(_bridge, 1, cursor, cancellationToken: TestContext.Current.CancellationToken));
        JsonElement detailed = Data(await SelectionTools.GetAsync(_bridge, response_format: "detailed", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("Name", detailed.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Set_PreservesLocatorsAndEveryReadbackFailure_WithoutWriteCall()
    {
        List<DrawingLocator> locators = [Row(1).Locator, Row(2).Locator];
        JsonElement data = Data(await SelectionTools.SetAsync(_bridge, locators, show_page: true, cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(data.GetProperty("exact").GetBoolean());
        Assert.Equal(2, Assert.Single(data.GetProperty("unselected").EnumerateArray()).GetProperty("oid").GetInt32());
        Assert.Equal(locators, Assert.IsType<SelectionArgs>(_bridge.LastArgs).Locators);
        Assert.Empty(_bridge.Writes);
        Assert.False(data.TryGetProperty("execution", out _));
    }

    [Fact]
    public async Task InvalidInput_DoesNotReachBridge_AndErrorsStayReadLevel()
    {
        ApiToolTests.AssertError(await SelectionTools.SetAsync(_bridge, [Row(1).Locator, Row(1).Locator], cancellationToken: TestContext.Current.CancellationToken));
        ApiToolTests.AssertError(await SelectionTools.SetAsync(_bridge, mode: "clear", show_page: true, cancellationToken: TestContext.Current.CancellationToken));
        ApiToolTests.AssertError(await SelectionTools.GetAsync(_bridge, response_format: "bad", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(_bridge.Calls);
        _bridge.Failure = new() { Reason = ErrorReason.CapabilityMissing, Message = "No UI." };
        JsonElement error = ApiToolTests.AssertError(await SelectionTools.GetAsync(_bridge, cancellationToken: TestContext.Current.CancellationToken), ErrorReason.CapabilityMissing);
        Assert.Equal(JsonValueKind.Null, error.GetProperty("execution").ValueKind);
    }

    [Fact]
    public void LargeReadback_IsBudgetedAndCanContinueWithoutDroppingMissingTargets()
    {
        var data = new SelectionData { Total = 200, Objects = Enumerable.Range(1, 200).Select(i => Row(i)).ToList(), Unselected = [Row(300).Locator] };
        var result = SelectionTools.Shape(data, 200, 0, "fingerprint", false, false);
        JsonElement content = Data(result);
        Assert.True(content.GetProperty("truncated").GetBoolean());
        Assert.NotNull(content.GetProperty("cursor").GetString());
        Assert.Single(content.GetProperty("unselected").EnumerateArray());
        Assert.True(ResultShaper.ResultTokens(content.GetRawText()) <= 5000);
        Assert.Equal(data.Objects[0].Locator.Digest, content.GetProperty("items")[0].GetProperty("locator").GetProperty("digest").GetString());
    }
}
