using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
namespace EplanMcp.Host.Tests.Tools;

public sealed partial class ApiTryToolTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task TrialResultUsesHandleThenShapedJsonThenLocatorThenCleanedText(int source)
    {
        _harness.Bridge.Handlers[OperationNames.ApiTryExecute] = _ => new ApiTryData
        {
            Handle = source == 0 ? new() { Handle = "h:" + new string('a', 32), Type = "Eplan.EplApi.DataModel.Pin" } : null,
            ResultJson = source <= 1 ? "{\"x\":2,\"y\":3}" : null,
            Locator = source <= 2 ? new() { Project = _harness.ProjectPath, Oid = 42 } : null,
            Value = "text\u202e",
        };
        JsonElement result = WriteToolHarness.Structured(await Call());
        JsonElement value = result.GetProperty("value");
        if (source == 0)
        { Assert.Equal("h:" + new string('a', 32), value.GetProperty("handle").GetString()); }
        else if (source == 1)
        { Assert.Equal(2, value.GetProperty("x").GetInt32()); }
        else if (source == 2)
        { Assert.Equal(42, value.GetProperty("oid").GetInt32()); }
        else
        { Assert.Equal("text", value.GetString()); }
        Assert.Contains("expire after 10 idle minutes", result.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RotateLineArray_ReservesPreflightAndCleanupTime_AndExecutesOnce()
    {
        const string Rotate = "O:Eplan.EplApi.HEServices.Edit.Rotate";
        object Locator(string page, int oid) => new { project = _harness.ProjectPath, page, type = 1, oid, digest = new string('a', 64) };
        JsonElement parameters = JsonSerializer.SerializeToElement(new
        {
            target = Locator(string.Empty, 1),
            plcmnts = new[] { Locator("=A/1", 2) },
            oPntStart = new { x = 0, y = 0 },
            angle = 90,
        });
        string signature = ApiTryCatalog.Shape(Rotate, parameters.EnumerateObject().Select(p => p.Name))![1];
        _harness.Bridge.Handlers[OperationNames.ApiTryPlan] = _ =>
        {
            WritePlan plan = _harness.Plan([new() { Action = "write", Kind = "unverified API", Name = Rotate, Property = signature }]);
            plan.Details = new() { ["status"] = "dispatchable", ["unverified"] = "true" };
            return plan;
        };
        JsonElement result = WriteToolHarness.Structured(await Tool().TryAsync(_progress, Rotate, "Line1", parameters, dry_run: false, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
        var write = Assert.Single(_harness.Bridge.Writes);
        Assert.Equal(TimeSpan.FromSeconds(120), write.Limit);
        var args = Assert.IsType<ApiTryArgs>(Assert.Single(_harness.Bridge.Calls, c => c.Operation == OperationNames.ApiTryExecute).Args);
        Assert.Equal(Rotate, args.Group);
        Assert.Equal(parameters.GetRawText(), args.Parameters);
        Assert.Equal("write unverified API " + Rotate + " = ; " + signature, Assert.Single(Assert.Single(_approval.Questions).Changes));
    }

    [Fact]
    public async Task CachedResultPagesNeverInvokeOrApproveTheWriteAgain()
    {
        _harness.Bridge.Handlers[OperationNames.ApiTryExecute] = _ => new ApiTryData
        {
            ResultJson = JsonSerializer.Serialize(Enumerable.Repeat(new string('x', 500), 200)),
        };
        ApiTryTools tool = Tool();
        JsonElement parameters = JsonSerializer.SerializeToElement(new { target = Target() });
        JsonElement first = WriteToolHarness.Structured(await tool.TryAsync(_progress, Group, "Line1", parameters, dry_run: false, cancellationToken: TestContext.Current.CancellationToken));
        string cursor = first.GetProperty("value").GetProperty("nextCursor").GetString()!;
        Assert.False(string.IsNullOrEmpty(cursor));
        int calls = _harness.Bridge.Calls.Count;
        Assert.Single(_approval.Questions);
        JsonElement next = WriteToolHarness.Structured(await tool.TryAsync(_progress, Group, "Line1", parameters, result_cursor: cursor, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("cached", next.GetProperty("status").GetString());
        Assert.Equal(ExecutionState.NotStarted, next.GetProperty("execution").GetString());
        Assert.NotEmpty(next.GetProperty("value").GetProperty("items").EnumerateArray());
        Assert.Equal(calls, _harness.Bridge.Calls.Count);
        Assert.Equal(JsonValueKind.Null, next.GetProperty("fingerprint").ValueKind);
        Assert.Empty(next.GetProperty("plannedChanges").EnumerateArray());
        Assert.Single(_approval.Questions);
    }
}
