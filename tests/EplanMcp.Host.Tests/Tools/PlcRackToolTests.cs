using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>eplan_plc_rack's Host side: snake_case card fields reach the Bridge, a preview only plans, a draw executes.</summary>
[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class PlcRackToolTests : IDisposable
{
    private readonly WriteToolHarness _harness = new();

    public PlcRackToolTests()
    {
        _harness.Bridge.Handlers[OperationNames.PlcRackPlan] = args => new WritePlan
        {
            Project = "Line1",
            ProjectPath = _harness.ProjectPath,
            Fingerprint = "f1",
            Changes = [new PlannedChange { Action = "create", Kind = "plc_card", Name = ((PlcRackArgs)args!).Cards![0].Tag! }],
        };
        _harness.Bridge.Handlers[OperationNames.PlcRackExecute] = args => new PlcRackData
        {
            Project = "Line1",
            RackPage = ((PlcRackArgs)args!).RackPage!,
            Cards = [new PlcRackCardResult { Tag = "=K1+UC1-KE1", Parts = ["SIE.6ES7131"] }],
        };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task APreview_BindsSnakeCaseCardFields_AndOnlyPlans()
    {
        JsonElement data = WriteToolHarness.Structured(await Rack(dryRun: true));

        var sent = (PlcRackArgs)_harness.Bridge.Calls.Single(c => c.Operation == OperationNames.PlcRackPlan).Args!;
        PlcRackCard card = Assert.Single(sent.Cards!);
        Assert.Equal(("=K1+UC1&EFA1/1", 5, "SIE.6ES7193", "-K1"), (card.OverviewPage, card.PictureVariant, card.ExtraParts![0], card.SubDevices![0].MacroSuffix));
        Assert.Equal(("not_started", "=K1+UC1-KE1"), (data.GetProperty("execution").GetString(), data.GetProperty("changes")[0].GetProperty("name").GetString()));
        Assert.DoesNotContain(_harness.Bridge.Calls, c => c.Operation == OperationNames.PlcRackExecute);
    }

    [Fact]
    public async Task ADraw_ExecutesWithThePlansFingerprint_AndReturnsTheCards()
    {
        JsonElement data = WriteToolHarness.Structured(await Rack(dryRun: false));

        var sent = (PlcRackArgs)_harness.Bridge.Calls.Single(c => c.Operation == OperationNames.PlcRackExecute).Args!;
        Assert.Equal("f1", sent.Fingerprint);
        Assert.Equal(("committed", "SIE.6ES7131"), (data.GetProperty("execution").GetString(), data.GetProperty("cards")[0].GetProperty("parts")[0].GetString()));
    }

    [Fact]
    public async Task MissingProject_IsRejectedBeforeCallingTheBridge()
    {
        await PreviewProtocolHarness.RunAsync(_harness, async client =>
        {
            var args = new Dictionary<string, object?>
            {
                ["rack_page"] = "=K1+UC1&EFS1/1",
                ["cards"] = new[] { new { tag = "=K1+UC1-KE1", part = "SIE.6ES7131", overview_page = "=K1+UC1&EFA1/1", picture_variant = 5 } },
            };
            CallToolResult call = await client.CallToolAsync(PlcRackTools.Tool, args, cancellationToken: TestContext.Current.CancellationToken);
            JsonElement error = WriteToolHarness.Error(call);
            Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
            Assert.Contains("Missing required argument 'project'", error.GetProperty("message").GetString(), StringComparison.Ordinal);
            Assert.Empty(_harness.Bridge.Calls);
        });
    }

    [Fact]
    public async Task ACardThatIsNoObject_IsAnInvalidArgument()
    {
        CallToolResult result = await PlcRackTools.PlcRackAsync(_harness.Pipeline, "=K1+UC1&EFS1/1", [JsonDocument.Parse("\"KE1\"").RootElement],
            project: "Line1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ErrorReason.InvalidArgument, WriteToolHarness.Error(result).GetProperty("reason").GetString());
        Assert.Empty(_harness.Bridge.Calls);
    }

    private Task<CallToolResult> Rack(bool dryRun) => PlcRackTools.PlcRackAsync(
        _harness.Pipeline,
        "=K1+UC1&EFS1/1",
        [JsonDocument.Parse("""
            {"tag": "=K1+UC1-KE1", "part": "SIE.6ES7131", "overview_page": "=K1+UC1&EFA1/1", "picture_variant": 5,
             "extra_parts": ["SIE.6ES7193"], "sub_devices": [{"macro_suffix": "-K1", "tag": "=K1+UC1-KEC1"}]}
            """).RootElement],
        project: "Line1",
        dry_run: dryRun,
        cancellationToken: TestContext.Current.CancellationToken);
}
