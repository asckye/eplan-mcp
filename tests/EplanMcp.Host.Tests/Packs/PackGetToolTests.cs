using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tests.Tools;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;
using EplanMcp.Packs.Scanning;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Packs;

/// <summary>eplan_pack_get on development packs in a temporary data folder (docs/tools/eplan_pack_get.md).</summary>
[Trait("Size", "Medium")]
public sealed class PackGetToolTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-pack-get-").FullName);
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 10, 21, 8, 30, 0, TimeSpan.Zero));
    private readonly FakeBridgeClient _bridge = new();

    private HostConfig Config => HostConfig.Defaults(_locations) with
    {
        PackSources = new Dictionary<string, string> { ["company-refs"] = Path.Combine(_locations.Root, "share") },
    };

    private PackLibrary Packs => new(_locations, Config, new AuditLog(_locations.Audit, _time), _time);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Get_CleanPack_SummarisesItWithGapsAndUnknownCapabilities()
    {
        WriteProject("share/Refs/Plant");
        Write("in/guide.md", "# Guide");
        await ImportAsync(new ScanInput(Path.Combine(_locations.Root, "share", "Refs"), Root: new ResourceReference { Root = "company-refs", Path = "Refs" }), new ScanInput(Path.Combine(_locations.Root, "in")));

        CallToolResult result = await GetAsync(null);

        ToolResultAssert.Succeeded(result);
        JsonElement data = result.StructuredContent!.Value;
        Assert.Equal(("dev", "eplanpack://acme.panels/dev/manifest"), (data.GetProperty("version").GetString(), data.GetProperty("uri").GetString()));
        Assert.Equal(3, data.GetProperty("resources").GetProperty("total").GetInt32());
        Assert.Equal(1, data.GetProperty("resources").GetProperty("referenced").GetInt32());
        Assert.Empty(data.GetProperty("problems").EnumerateArray());
        Assert.StartsWith("acme.panels@dev:gap/eplan-compatibility-unchecked-", Assert.Single(data.GetProperty("gaps").EnumerateArray()).GetProperty("asset").GetString(), StringComparison.Ordinal);
        Assert.Equal(1, data.GetProperty("slots").GetProperty("docs").GetProperty("candidate").GetInt32());
        Assert.False(data.TryGetProperty("resourceList", out JsonElement list) && list.ValueKind != JsonValueKind.Null);
        Assert.Equal("Pack 'acme.panels' dev: 3 resources, 0 problems, 1 open gap, requirements met.", ((TextContentBlock)result.Content[0]).Text);
    }

    [Fact]
    public async Task Get_ChangedFilesAndReferences_AreProblemsNotErrors()
    {
        WriteProject("share/Refs/Plant");
        Write("in/guide.md", "# Guide");
        await ImportAsync(new ScanInput(Path.Combine(_locations.Root, "share", "Refs"), Root: new ResourceReference { Root = "company-refs", Path = "Refs" }), new ScanInput(Path.Combine(_locations.Root, "in")));
        Write("share/Refs/Plant/Plant.edb/Page.eod", "changed pages");
        File.AppendAllText(Path.Combine(_locations.PacksDev, "acme.panels", "resources", "docs", "in", "guide.md"), "!");

        CallToolResult result = await GetAsync(null, "detailed");

        ToolResultAssert.Succeeded(result);
        string[] locations = [.. result.StructuredContent!.Value.GetProperty("problems").EnumerateArray().Select(p => p.GetProperty("location").GetString()!)];
        Assert.Equal(["resources/docs/in/guide.md", "resources/projects/Refs/Plant/Plant.elk"], locations.Order(StringComparer.Ordinal));
        Assert.Equal(3, result.StructuredContent!.Value.GetProperty("resourceList").GetArrayLength());
    }

    [Fact]
    public async Task Get_Capabilities_ComeFromTheHandshake()
    {
        Write("in/guide.md", "# Guide");
        await ImportAsync(new ScanInput(Path.Combine(_locations.Root, "in")));
        string manifestPath = Path.Combine(_locations.PacksDev, "acme.panels", PackManifest.FileName);
        PackManifest manifest = PackJson.ReadManifest(File.ReadAllBytes(manifestPath)) with
        {
            Compatibility = new PackCompatibility { EplanVersions = "2026.0", Capabilities = ["op:module.insert", "A:backup/PROJECT"] },
        };
        File.WriteAllBytes(manifestPath, PackJson.Serialize(manifest));
        _bridge.Status = new BridgeStatus([], new HandshakeResult { Eplan = new EplanInstanceInfo { Version = "2026.0.3.25421" }, Operations = ["page.list"] }, null);

        JsonElement data = (await GetAsync(null)).StructuredContent!.Value;

        Assert.Equal(["met", "missing", "unchecked"], data.GetProperty("capabilities").EnumerateArray().Select(c => c.GetProperty("status").GetString()));
        Assert.False(data.GetProperty("capabilitiesMet").GetBoolean());
    }

    [Theory]
    [InlineData("acme.panels", "1.0.0", ErrorReason.ResourceNotFound)]
    [InlineData("other", null, ErrorReason.ResourceNotFound)]
    [InlineData("acme.panels", "1.0", ErrorReason.InvalidArgument)]
    [InlineData("Acme", null, ErrorReason.InvalidArgument)]
    public async Task Get_UnknownOrMalformed_IsAnError(string pack, string? version, string reason)
    {
        Write("in/guide.md", "# Guide");
        await ImportAsync(new ScanInput(Path.Combine(_locations.Root, "in")));

        CallToolResult result = await PackTools.PackGetAsync(Packs, _bridge, new ToolProfileChoice(ToolProfile.Full), pack, version, ToolArguments.Concise, Cancel);

        Assert.True(result.IsError);
        Assert.Equal(reason, JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement.GetProperty("error").GetProperty("reason").GetString());
    }

    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);

    private Task<CallToolResult> GetAsync(string? version, string? format = null) =>
        PackTools.PackGetAsync(Packs, _bridge, new ToolProfileChoice(ToolProfile.Full), "acme.panels", version, format ?? ToolArguments.Concise, Cancel);

    private async Task ImportAsync(params ScanInput[] inputs)
    {
        ScanResult scan = await ResourceScanner.ScanAsync(inputs, Cancel);
        await Packs.Dev.ImportAsync("acme.panels", "ACME panels", scan, false, "eplanmcp test", _time.GetUtcNow(), Cancel);
    }

    private void Write(string relative, string content)
    {
        string path = Path.Combine(_locations.Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void WriteProject(string folder)
    {
        string name = Path.GetFileName(folder);
        Write($"{folder}/{name}.elk", $"{name}.edb\n18.0\n");
        Write($"{folder}/{name}.edb/ProjectInfo.xml", "<Properties />");
        Write($"{folder}/{name}.edb/Page.eod", "pages");
    }
}
