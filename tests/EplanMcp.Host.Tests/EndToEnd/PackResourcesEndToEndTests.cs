using System.Text.Json;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;
using EplanMcp.Packs.Scanning;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.EndToEnd;

/// <summary>
/// The pack resource templates of M6 design 4.5.2 over the real protocol: an MCP client starts <c>eplanmcp serve</c>
/// with a temporary data folder holding a development pack, lists the templates and reads each kind of resource.
/// </summary>
[Trait("Size", "Medium")]
public sealed class PackResourcesEndToEndTests : IAsyncLifetime
{
    private readonly string _home = Directory.CreateTempSubdirectory("eplanmcp-pack-resources-").FullName;
    private McpClient? _client;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        string input = Path.Combine(_home, "in");
        Directory.CreateDirectory(input);
        await File.WriteAllTextAsync(Path.Combine(input, "Motor.ema"), "<EplanPxfRoot Type=\"WindowMacro\" Version=\"2026.0.25356\" />", Cancel);
        await File.WriteAllBytesAsync(Path.Combine(input, "page.png"), [0x89, 0x50, 0x4E, 0x47, 1, 2, 3], Cancel);
        ScanResult scan = await ResourceScanner.ScanAsync([new ScanInput(input)], Cancel);
        await new DevPackStore(Path.Combine(_home, "packs-dev")).ImportAsync("acme.panels", "ACME panels", scan, false, "eplanmcp test", DateTimeOffset.UtcNow, Cancel);
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "eplanmcp",
            Command = Path.Combine(AppContext.BaseDirectory, "eplanmcp.exe"),
            Arguments = ["serve"],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["EPLANMCP_HOME"] = _home,
                [PipeBridgeDiscovery.PrefixVariable] = "EplanMcpPackE2E." + Guid.NewGuid().ToString("N") + ".",
            },
        });
        _client = await McpClient.CreateAsync(transport, cancellationToken: Cancel);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        Directory.Delete(_home, recursive: true);
    }

    [Fact]
    public async Task Templates_IncludePacksAndOwnedPlans()
    {
        IList<McpClientResourceTemplate> templates = await _client!.ListResourceTemplatesAsync(cancellationToken: Cancel);

        Assert.Equal(
            new[] { PackResources.ManifestTemplate, PackResources.PreviewTemplate, PackResources.AssetTemplate, PlanTools.Template, PlanApplyTools.Template }.Order(StringComparer.Ordinal),
            templates.Select(t => t.UriTemplate).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Read_ManifestAssetAndPreview_GiveWhatTheToolsGive()
    {
        ReadResourceResult manifest = await _client!.ReadResourceAsync("eplanpack://acme.panels/dev/manifest", cancellationToken: Cancel);
        ReadResourceResult asset = await _client.ReadResourceAsync("eplanpack://acme.panels/dev/macro/in%2FMotor.ema", cancellationToken: Cancel);
        ReadResourceResult preview = await _client.ReadResourceAsync("eplanpack://acme.panels/dev/previews/in%2Fpage.png", cancellationToken: Cancel);

        Assert.Equal("acme.panels", JsonDocument.Parse(((TextResourceContents)manifest.Contents[0]).Text).RootElement.GetProperty("id").GetString());
        JsonElement described = JsonDocument.Parse(((TextResourceContents)asset.Contents[0]).Text).RootElement;
        Assert.Equal(("acme.panels@dev:macro/in/Motor.ema", true), (described.GetProperty("asset").GetString(), described.GetProperty("available").GetBoolean()));
        var image = (BlobResourceContents)preview.Contents[0];
        Assert.Equal(("image/png", 7), (image.MimeType, image.DecodedData.Length));
    }

    [Fact]
    public async Task Read_Missing_IsAnError()
    {
        await Assert.ThrowsAsync<McpProtocolException>(() => _client!.ReadResourceAsync("eplanpack://acme.panels/9.9.9/manifest", cancellationToken: Cancel).AsTask());
    }
}
