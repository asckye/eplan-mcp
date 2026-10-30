using System.Text.Json;
using EplanMcp.Large.Tests;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class ToolUsageExampleTests
{
    [Fact]
    public async Task AcceptanceDriver_AllBatches_MatchRegisteredToolArguments()
    {
        var fake = new AcceptanceRedrawDriverTests.DrawingTools { ConnectWires = true };
        var driver = new AcceptanceRedrawDriver(fake.CallAsync, "LargeWrite", AcceptanceDrawingList.Read(AcceptanceDrawingListTests.Fixture()), "schema", _ => { });
        await driver.DrawAsync();
        await driver.ExportAsync();
        foreach ((string name, JsonElement args) in fake.Requests)
        {
            JsonElement schema = Assert.Single(Tools, tool => tool.Name == name).InputSchema;
            Assert.Empty(Violations(schema, args, null));
        }
    }
}
