using System.Text.Json;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tests.Tools;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;
using EplanMcp.Packs.Scanning;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Packs;

/// <summary>eplan_asset_search on development and installed packs in a temporary data folder (docs/tools/eplan_asset_search.md).</summary>
[Trait("Size", "Medium")]
public sealed class AssetSearchToolTests : IDisposable
{
    private const string Macro = "<EplanPxfRoot Type=\"WindowMacro\" Version=\"2026.0.25356\" />";
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-asset-search-").FullName);
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 10, 21, 8, 30, 0, TimeSpan.Zero));
    private readonly FakeBridgeClient _bridge = new();

    private PackLibrary Packs => new(_locations, HostConfig.Defaults(_locations), new AuditLog(_locations.Audit, _time), _time);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Search_DevAndHighestVersion_ByTextAndKind()
    {
        Write("in/macros/Motor_DOL.ema", Macro);
        Write("in/macros/Valve.ema", Macro);
        Write("in/guide.md", "# Motor guide");
        await ImportAsync("in");
        await PackTools.PackBuildAsync(Packs, ReviewFixtures.Service(Packs), "acme.panels", "1.0.0", Cancel);

        CallToolResult result = await SearchAsync(text: "motor", kind: "macro");

        ToolResultAssert.Succeeded(result);
        Assert.Equal(
            ["acme.panels@1.0.0:macro/in/macros/Motor_DOL.ema", "acme.panels@dev:macro/in/macros/Motor_DOL.ema"],
            Assets(result).Order(StringComparer.Ordinal));
        Assert.Equal("Found 2 of 2 assets.", ((TextContentBlock)result.Content[0]).Text);
        Assert.Equal(["acme.panels@dev:document/in/guide.md"], Assets(await SearchAsync(text: "guide", pack: "acme.panels", version: "dev")));
        Assert.Equal("candidate", (await SearchAsync(kind: "document", pack: "acme.panels", version: "dev")).StructuredContent!.Value.GetProperty("assets")[0].GetProperty("standing").GetString());
    }

    [Fact]
    public async Task Search_TagsGapsAndUnconfiguredSources()
    {
        Write("in/macros/Motor.ema", Macro.Replace("/>", "SourceProject=\"D:\\Projects\\Plant.elk\" />", StringComparison.Ordinal));
        await ImportAsync("in");
        string manifestPath = Path.Combine(_locations.PacksDev, "acme.panels", PackManifest.FileName);
        PackManifest manifest = PackJson.ReadManifest(File.ReadAllBytes(manifestPath));
        PackResource referenced = Manifests("resources/projects/Plant/Plant.elk") with { Storage = ResourceStorage.Referenced, Reference = new ResourceReference { Root = "gone", Path = "Plant/Plant.elk" } };
        File.WriteAllBytes(manifestPath, PackJson.Serialize(manifest with { Resources = [.. manifest.Resources.Append(referenced).OrderBy(r => r.Path, StringComparer.Ordinal)] }));

        JsonElement gap = Assert.Single((await SearchAsync(kind: "gap", tag: "import")).StructuredContent!.Value.GetProperty("assets").EnumerateArray());
        JsonElement project = Assert.Single((await SearchAsync(kind: "project")).StructuredContent!.Value.GetProperty("assets").EnumerateArray());

        Assert.StartsWith("acme.panels@dev:gap/source-project-path-", gap.GetProperty("asset").GetString(), StringComparison.Ordinal);
        Assert.Equal(["window_macro"], Assets(await SearchAsync(tag: "WINDOW_MACRO")).Select(_ => "window_macro"));
        Assert.False(project.GetProperty("available").GetBoolean());
        Assert.Contains("packSources", project.GetProperty("why").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "dev", "", "goes with 'pack'")]
    [InlineData("acme.panels", "1.0", "", "goes with 'pack'")]
    [InlineData("", "", "2026.x", "eplan_version")]
    public async Task Search_BadArguments_AreInvalid(string pack, string version, string eplan, string reason)
    {
        CallToolResult result = await AssetTools.AssetSearchAsync(Packs, _bridge, new ToolProfileChoice(ToolProfile.Full), pack: pack, version: version, eplan_version: eplan, cancellationToken: Cancel);

        Assert.True(result.IsError);
        Assert.Contains(reason, ((TextContentBlock)result.Content[0]).Text, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        foreach (string file in Directory.EnumerateFiles(_locations.Root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_locations.Root, recursive: true);
    }

    private static PackResource Manifests(string path) => new()
    {
        Path = path,
        Kind = ResourceKind.Project,
        Sha256 = new string('a', 64),
        Bytes = 10,
        Files = 2,
        Storage = ResourceStorage.Bundled,
    };

    private static IEnumerable<string> Assets(CallToolResult result) =>
        result.StructuredContent!.Value.GetProperty("assets").EnumerateArray().Select(a => a.GetProperty("asset").GetString()!);

    private Task<CallToolResult> SearchAsync(string text = "", string kind = AssetTools.AnyKind, string pack = "", string version = "", string tag = "") =>
        AssetTools.AssetSearchAsync(Packs, _bridge, new ToolProfileChoice(ToolProfile.Full), text, kind, pack, version, tag, cancellationToken: Cancel);

    private async Task ImportAsync(string folder)
    {
        ScanResult scan = await ResourceScanner.ScanAsync([new ScanInput(Path.Combine(_locations.Root, folder))], Cancel);
        await Packs.Dev.ImportAsync("acme.panels", "ACME panels", scan, false, "eplanmcp test", _time.GetUtcNow(), Cancel);
    }

    private void Write(string relative, string content)
    {
        string path = Path.Combine(_locations.Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
