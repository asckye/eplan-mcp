using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;
using EplanMcp.Packs.Knowledge;
using EplanMcp.Packs.Scanning;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Packs;

/// <summary>eplan_asset_update on development packs in a temporary data folder (docs/tools/eplan_asset_update.md).</summary>
[Trait("Size", "Medium")]
public sealed class AssetUpdateToolTests : IDisposable
{
    private const string Macro = "<EplanPxfRoot Type=\"WindowMacro\" Version=\"2026.0.25356\" />";
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-asset-update-").FullName);
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 10, 21, 8, 30, 0, TimeSpan.Zero));

    private PackLibrary Packs => new(_locations, HostConfig.Defaults(_locations), new AuditLog(_locations.Audit, _time), _time);

    private string Pack => Path.Combine(_locations.PacksDev, "acme.panels");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Update_Candidate_IsProposedWithLocalReferencesAndOrigin()
    {
        await ImportAsync();

        CallToolResult result = await UpdateAsync("candidate", "motor-overload", """{"statement": "Motors have overload relays.", "basis": ["acme.panels@dev:macro/in/Motor.ema", "fact:resources/macros/in/Motor.ema#eplan_version"], "confidence": "medium"}""", """{"asset": "acme.panels@dev:macro/in/Motor.ema", "page": "=AP+ST1/3"}""");

        ToolResultAssert.Succeeded(result);
        JsonElement data = result.StructuredContent!.Value;
        Assert.Equal((true, "candidate", "acme.panels@dev:candidate/motor-overload"), (data.GetProperty("created").GetBoolean(), data.GetProperty("standing").GetString(), data.GetProperty("asset").GetString()));
        KnowledgeDocument stored = KnowledgeDocument.Read(File.ReadAllBytes(Path.Combine(Pack, "knowledge", "candidate", "motor-overload.json")));
        Assert.Equal(("proposed", "macro/in/Motor.ema", "macro/in/Motor.ema"), ((string?)stored.Spec["status"], (string?)stored.Spec["basis"]![0], stored.Origin!.Asset));
        PackCheckResult check = await PackVerifier.CheckDirectoryAsync(Pack, Cancel);
        Assert.Empty(check.Problems);
        Assert.Equal(KnowledgeStanding.Candidate, Assert.Single(check.Manifest!.Rules).Standing);
    }

    [Fact]
    public async Task Update_SameNameAgain_Replaces()
    {
        await ImportAsync();
        await UpdateAsync("gap", "terminals", """{"missing": "No terminal rules."}""");

        CallToolResult again = await UpdateAsync("gap", "terminals", """{"missing": "No terminal numbering rules."}""");

        Assert.False(again.StructuredContent!.Value.GetProperty("created").GetBoolean());
        Assert.Equal("No terminal numbering rules.", (string?)KnowledgeDocument.Read(File.ReadAllBytes(Path.Combine(Pack, "knowledge", "gap", "terminals.json"))).Spec["missing"]);
    }

    [Theory]
    [InlineData("candidate", """{"statement": "x", "basis": ["acme.panels@dev:macro/none.ema"], "confidence": "low"}""", "/spec/basis/0")]
    [InlineData("candidate", """{"statement": "x", "basis": ["acme.panels@dev:macro/in/Motor.ema"], "confidence": "low", "status": "accepted"}""", "/spec/status")]
    [InlineData("module", """{"macro": "acme.panels@dev:document/in/guide.md"}""", "/spec/macro")]
    public async Task Update_BadSpec_NamesThePointerAndWritesNothing(string kind, string spec, string location)
    {
        await ImportAsync();

        CallToolResult result = await UpdateAsync(kind, "x", spec);

        JsonElement error = Error(result, ErrorReason.InvalidArgument);
        Assert.Equal(location, error.GetProperty("metadata").GetProperty("pointer").GetString());
        Assert.False(Directory.Exists(Path.Combine(Pack, "knowledge", kind)));
    }

    [Theory]
    [InlineData("rule", "only the engineer makes a rule official")]
    [InlineData("fact_set", "facts come only from extraction")]
    [InlineData("config_schema", "must be candidate, module, material_mapping or gap")]
    [InlineData("config_layer", "must be candidate, module, material_mapping or gap")]
    [InlineData("workflow", "must be candidate, module, material_mapping or gap")]
    [InlineData("sample", "must be candidate, module, material_mapping or gap")]
    public async Task Update_FactsAndRules_AreNeverWritten(string kind, string reason)
    {
        await ImportAsync();

        Assert.Contains(reason, Error(await UpdateAsync(kind, "x", "{}"), ErrorReason.InvalidArgument).GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_NoDevPack_IsResourceNotFound()
    {
        Error(await UpdateAsync("gap", "x", """{"missing": "x"}"""), ErrorReason.ResourceNotFound);
    }

    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);

    private Task<CallToolResult> UpdateAsync(string kind, string name, string spec, string? origin = null) =>
        AssetTools.AssetUpdateAsync(Packs, "acme.panels", kind, name, JsonDocument.Parse(spec).RootElement, origin: origin is null ? null : JsonDocument.Parse(origin).RootElement, cancellationToken: Cancel);

    private static JsonElement Error(CallToolResult result, string reason)
    {
        Assert.True(result.IsError);
        JsonElement error = JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement.GetProperty("error");
        Assert.Equal(reason, error.GetProperty("reason").GetString());
        return error;
    }

    private async Task ImportAsync()
    {
        string input = Path.Combine(_locations.Root, "in");
        Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "Motor.ema"), Macro);
        File.WriteAllText(Path.Combine(input, "guide.md"), "# Guide");
        ScanResult scan = await ResourceScanner.ScanAsync([new ScanInput(input)], Cancel);
        await Packs.Dev.ImportAsync("acme.panels", "ACME panels", scan, false, "eplanmcp test", _time.GetUtcNow(), Cancel);
    }
}
