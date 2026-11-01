using EplanMcp.Contracts;
namespace EplanMcp.Host.Tests.Tools;

public sealed partial class ApiTryToolTests
{
    [Fact]
    public async Task CollectionResultsUseMainPagingAndKeepApprovalAndAudit()
    {
        _harness.Bridge.Handlers[OperationNames.ApiTryExecute] = _ => new ApiTryData
        {
            ResultJson = "[{\"handle\":\"h:" + new string('b', 32) + "\",\"type\":\"Eplan.EplApi.DataModel.Pin\"}]",
            HandleSource = Group,
        };
        var result = WriteToolHarness.Structured(await Call());
        Assert.Single(result.GetProperty("value").GetProperty("items").EnumerateArray());
        Assert.Equal(1, result.GetProperty("value").GetProperty("total").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, result.GetProperty("value").GetProperty("nextCursor").ValueKind);
        Assert.Single(_approval.Questions);
        Assert.Contains("handleSource", File.ReadAllText(Assert.Single(Directory.GetFiles(_harness.Locations.Audit, "*.jsonl"))), StringComparison.Ordinal);
    }
}
