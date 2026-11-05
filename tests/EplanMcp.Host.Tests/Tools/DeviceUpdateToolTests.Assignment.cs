using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class DeviceUpdateToolTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task AssignedFunctionsAreReturnedFromCommittedReadback(int count)
    {
        _harness.Bridge.Handlers[OperationNames.DeviceUpdateExecute] = _ => new DeviceUpdateData
        { Project = "Line1", Tag = "+A1-K1", Part = "SIE.3RT2015-1BB41-1AA0", AssignedFunctions = count };
        JsonElement result = WriteToolHarness.Structured(await DeviceWriteTools.DeviceUpdateAsync(_harness.Pipeline,
            "Line1", "+A1-K1", dry_run: false, part: "SIE.3RT2015-1BB41-1AA0", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(count, result.GetProperty("assignedFunctions").GetInt32());
        Assert.Equal("committed", result.GetProperty("execution").GetString());
    }
}
