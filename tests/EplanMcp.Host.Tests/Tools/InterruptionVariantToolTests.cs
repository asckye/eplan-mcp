using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Bridge.Core.Tests.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
public sealed class InterruptionVariantToolTests
{
    private static readonly int[] _position = [60, 60];
    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExplicitVariant_PreservesExactRequest_AndPreviewDefault(bool? dry)
    {
        using var harness = new WriteToolHarness();
        var fixture = new ConnectionCreateFixture();
        fixture.Scene.Project.Path = harness.ProjectPath;
        fixture.Scene.Page.Project = harness.ProjectPath;
        fixture.Symbols.Add(ConnectionCreateFixture.Variant("BP", "InterruptionPoint", 0, "Right", "Left"));
        fixture.Symbols.Add(ConnectionCreateFixture.Variant("BP", "InterruptionPoint", 2, "Left", "Right"));
        JObject Preview(string json) => EplanMcp.Bridge.Core.Operations.ConnectionCreatePlanner.Plan(json, fixture.Scene, fixture);
        harness.Bridge.Handlers[OperationNames.ConnectionCreatePlan] = args =>
        {
            JObject preview = Preview(JsonSerializer.Serialize(args));
            WritePlan plan = harness.Plan([new() { Action = "create", Kind = "connections", Name = "BP 0" }]);
            plan.Fingerprint = (string)preview["fingerprint"]!;
            plan.Details = new() { ["preview"] = preview.ToString() };
            return plan;
        };
        harness.Bridge.Handlers[OperationNames.ConnectionCreateExecute] = args => JsonNode.Parse(Preview(JObject.Parse(JsonSerializer.Serialize(args))["request"]!.ToString()).ToString())!;
        JsonElement item = JsonSerializer.SerializeToElement(new { kind = "interruption_point", name = "=GAA-2L1", direction = "Right", symbol = "BP", symbol_variant = 0, position = _position });
        var call = dry is null
            ? await ConnectionCreateTools.CreateAsync(harness.Pipeline, harness.Bridge, "Line1", ConnectionCreateFixture.PageName, [item], generate: false, cancellationToken: TestContext.Current.CancellationToken)
            : await ConnectionCreateTools.CreateAsync(harness.Pipeline, harness.Bridge, "Line1", ConnectionCreateFixture.PageName, [item], dry_run: dry.Value, generate: false, cancellationToken: TestContext.Current.CancellationToken);
        JsonElement result = WriteToolHarness.Structured(call);
        Assert.Equal(dry != false, result.GetProperty("dryRun").GetBoolean());
        Assert.Equal(0, result.GetProperty("symbols")[0].GetProperty("symbol").GetProperty("variant").GetInt32());
        Assert.Equal(dry == false ? 1 : 0, harness.Bridge.Writes.Count);
    }
}
