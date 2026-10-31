using System.Text.Json;
using EplanMcp.Large.Tests;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class ToolUsageExampleTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AcceptanceDriver_AllBatches_MatchRegisteredToolArguments(bool explicitWires, bool preflight)
    {
        var fake = new AcceptanceRedrawDriverTests.DrawingTools { ConnectWires = true };
        JsonElement list = explicitWires ? AcceptanceRedrawDriverTests.ExplicitWiresList() : AcceptanceDrawingList.Read(AcceptanceDrawingListTests.Fixture());
        var driver = new AcceptanceRedrawDriver(fake.CallAsync, "LargeWrite", list, "schema", _ => { }, keepGoing: preflight, preflight: preflight);
        if (preflight)
        { await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(driver.DrawAsync); }
        else
        {
            await driver.DrawAsync();
            await driver.ExportAsync();
        }
        foreach ((string name, JsonElement args) in fake.Requests)
        {
            JsonElement schema = Assert.Single(Tools, tool => tool.Name == name).InputSchema;
            Assert.Empty(Violations(schema, args, null));
        }
    }
}
