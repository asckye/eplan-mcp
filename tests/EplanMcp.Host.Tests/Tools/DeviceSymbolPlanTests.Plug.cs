using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class DeviceSymbolPlanTests
{
    [Theory]
    [InlineData("PDEF", "TextFunction", "other", "ConnectorDefText", true)]
    [InlineData("PDEF", "TextFunction", "other", "ConnectorDefText", false)]
    public async Task PlugSymbols_PreviewOrCommitWithCategoryAndOneUndo(string name, string type, string kind, string category, bool preview)
    {
        _store.Symbols.Add(new()
        {
            Library = "SPECIAL",
            Name = name,
            Variant = 0,
            Type = type,
            Kind = kind,
            FunctionCategory = category,
            ConnectionPoints = name == "PDEF" ? [] : [new() { Location = [0, 0] }, new() { Location = [0, 4] }]
        });
        var response = await DeviceWriteTools.DevicePlaceAsync(_harness.Pipeline, _harness.Bridge,
            "Line1", Page, 80, 160, tag: "-X9", library: "SPECIAL", symbol: name, symbol_variant: 0,
            connection_designations: name == "PDEF" ? null : ["1", "2"], dry_run: preview,
            cancellationToken: TestContext.Current.CancellationToken);
        JsonElement result = WriteToolHarness.Structured(response);
        Assert.Equal(category, result.GetProperty("symbol").GetProperty("functionCategory").GetString());
        Assert.Equal(preview ? ExecutionState.NotStarted : ExecutionState.Committed, result.GetProperty("execution").GetString());
        if (preview)
        {
            Assert.Empty(_placement.Calls);
            Assert.Empty(_harness.Bridge.Writes);
        }
        else
        {
            Assert.Equal(category, result.GetProperty("device").GetProperty("category").GetString());
            Assert.Single(_placement.Calls, call => call == "Commit");
            Assert.Single(_placement.Calls, call => call == "CloseUndo");
            Assert.DoesNotContain("Abort", _placement.Calls);
            Assert.Equal(name == "PDEF" ? null : ["1", "2"], _placement.Args!.ConnectionDesignations);
        }
    }
}
