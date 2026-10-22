using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tests.Tools;
using EplanMcp.Host.Tools;
using EplanMcp.Packs.Scanning;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Packs;

/// <summary>
/// eplan_asset_get on development packs in a temporary data folder (docs/tools/eplan_asset_get.md): sources and facts
/// trace a resource back (M6 design 4.17, scenario 3, resources); missing ones are RESOURCE_NOT_FOUND (scenario 8).
/// </summary>
[Trait("Size", "Medium")]
public sealed class AssetGetToolTests : IDisposable
{
    private const string Macro = "<EplanPxfRoot Type=\"WindowMacro\" Version=\"2026.0.25356\" />";
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-asset-get-").FullName);
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 10, 21, 8, 30, 0, TimeSpan.Zero));
    private readonly FakeBridgeClient _bridge = new();

    private PackLibrary Packs => new(_locations, HostConfig.Defaults(_locations), new AuditLog(_locations.Audit, _time), _time);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Get_Macro_TracesBackToItsInputAndFacts()
    {
        Write("ref/macros/Motor_DOL.ema", Macro);
        await ImportAsync("ref");

        CallToolResult result = await GetAsync("acme.panels@dev:macro/ref/macros/Motor_DOL.ema");

        ToolResultAssert.Succeeded(result);
        JsonElement data = result.StructuredContent!.Value;
        Assert.Equal(("ref", "macros/Motor_DOL.ema"), (data.GetProperty("source").GetProperty("input").GetString(), data.GetProperty("source").GetProperty("path").GetString()));
        Assert.Contains(data.GetProperty("facts").EnumerateArray(), f => f.GetProperty("name").GetString() == "eplan_version" && f.GetProperty("evidence").GetString() == "EplanPxfRoot Version attribute");
        Assert.True(data.GetProperty("available").GetBoolean());
        Assert.Equal("eplanpack://acme.panels/dev/macro/ref%2Fmacros%2FMotor_DOL.ema", data.GetProperty("uri").GetString());
        Assert.Equal(AssetTools.PreviewNote, data.GetProperty("previewNote").GetString());
        Assert.Contains("placeholders", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Equal("Asset acme.panels@dev:macro/ref/macros/Motor_DOL.ema: window_macro, bundled, available, 2 facts.", ((TextContentBlock)result.Content[0]).Text);
    }

    [Fact]
    public async Task Get_GapDocument_HasItsOriginAndContent_AndAChangedFileIsUnavailable()
    {
        Directory.CreateDirectory(Path.Combine(_locations.Root, "ref"));
        File.WriteAllBytes(Path.Combine(_locations.Root, "ref", "basic.zw9"), [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0x00, 0x04]);
        await ImportAsync("ref");
        string gap = (await AssetTools.AssetSearchAsync(Packs, _bridge, new ToolProfileChoice(ToolProfile.Full), kind: "gap", cancellationToken: Cancel))
            .StructuredContent!.Value.GetProperty("assets")[0].GetProperty("asset").GetString()!;

        CallToolResult got = await GetAsync(gap);
        ToolResultAssert.Succeeded(got);
        JsonElement data = got.StructuredContent!.Value;
        File.AppendAllText(Path.Combine(_locations.PacksDev, "acme.panels", "resources", "basic-projects", "ref", "basic.zw9"), "!");
        JsonElement changed = (await GetAsync("acme.panels@dev:basic_project/ref/basic.zw9")).StructuredContent!.Value;

        Assert.Equal("basic_project/ref/basic.zw9", data.GetProperty("origin").GetProperty("asset").GetString());
        Assert.Equal("open", data.GetProperty("spec").GetProperty("status").GetString());
        Assert.False(changed.GetProperty("available").GetBoolean());
        Assert.Contains("does not match pack.json", changed.GetProperty("problems")[0].GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("acme.panels@dev:macro/nothing.ema", ErrorReason.ResourceNotFound)]
    [InlineData("other@dev:macro/x.ema", ErrorReason.ResourceNotFound)]
    [InlineData("acme.panels@1.0.0:macro/ref/macros/Motor_DOL.ema", ErrorReason.ResourceNotFound)]
    [InlineData("acme.panels:macro/x.ema", ErrorReason.InvalidArgument)]
    [InlineData("acme.panels@1.0:macro/x.ema", ErrorReason.InvalidArgument)]
    public async Task Get_UnknownOrMalformedAsset_IsAnError(string asset, string reason)
    {
        Write("ref/macros/Motor_DOL.ema", Macro);
        await ImportAsync("ref");

        CallToolResult result = await GetAsync(asset);

        Assert.True(result.IsError);
        Assert.Equal(reason, JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement.GetProperty("error").GetProperty("reason").GetString());
    }

    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);

    private Task<CallToolResult> GetAsync(string asset) => AssetTools.AssetGetAsync(Packs, _bridge, new ToolProfileChoice(ToolProfile.Full), asset, Cancel);

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
