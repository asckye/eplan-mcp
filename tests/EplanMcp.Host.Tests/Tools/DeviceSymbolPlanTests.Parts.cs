using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class DeviceSymbolPlanTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task BcpPart_ExactLiveRequest_PreviewsCommitsOrRollsBackBeforeClosingUndo(bool preview, bool fail)
    {
        _store.Names.Add("+A1-WE1");
        _store.Parts.Parts.Add(new() { PartNumber = "RIT.3451500", Variant = "1" });
        _store.Symbols.Add(new()
        {
            Library = "SPECIAL",
            Name = "BCP",
            Variant = 2,
            Type = "Function",
            Kind = "function",
            FunctionCategory = "Busbar"
        });
        _placement.FailAt = fail ? "AttachPart" : null;
        var response = await DeviceWriteTools.DevicePlaceAsync(_harness.Pipeline, _harness.Bridge,
            "Line1", Page, 100, 120, tag: "+A1-WE1", part: "RIT.3451500", variant: "1",
            library: "SPECIAL", symbol: "BCP", symbol_variant: 2, join_device: true, dry_run: preview,
            cancellationToken: TestContext.Current.CancellationToken);
        if (fail)
        {
            JsonElement error = WriteToolHarness.Error(response);
            Assert.Equal(ExecutionState.RolledBack, error.GetProperty("execution").GetString());
            Assert.Contains("Abort", _placement.Calls);
            Assert.DoesNotContain("CloseUndo", _placement.Calls);
            Assert.Equal(["+A1-WE1"], _store.Names);
            return;
        }
        JsonElement result = WriteToolHarness.Structured(response);
        Assert.Equal("RIT.3451500", result.GetProperty("part").GetString());
        Assert.Equal("1", result.GetProperty("variant").GetString());
        Assert.Equal(preview ? ExecutionState.NotStarted : ExecutionState.Committed, result.GetProperty("execution").GetString());
        if (preview)
        { Assert.Empty(_placement.Calls); }
        else
        {
            Assert.True(_placement.Calls.IndexOf("Commit") < _placement.Calls.IndexOf("AttachPart"));
            Assert.True(_placement.Calls.IndexOf("AttachPart") < _placement.Calls.IndexOf("CloseUndo"));
            Assert.Single(_placement.Calls, call => call == "CommittedReadback");
        }
    }
}
