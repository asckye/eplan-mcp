using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;
using EplanMcp.Packs.Scanning;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Packs;

/// <summary>eplan_pack_build on development packs in a temporary data folder (docs/tools/eplan_pack_build.md).</summary>
[Trait("Size", "Medium")]
public sealed class PackBuildToolTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-pack-build-").FullName);
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 10, 21, 8, 30, 0, TimeSpan.Zero));

    private HostConfig Config => HostConfig.Defaults(_locations) with
    {
        PackSources = new Dictionary<string, string> { ["company-refs"] = Path.Combine(_locations.Root, "share") },
    };

    private PackLibrary Packs => new(_locations, Config, new AuditLog(_locations.Audit, _time), _time);

    private string Installed => Path.Combine(_locations.Packs, "acme.panels", "1.0.0");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Build_InstallsAReadOnlyVersion_KeepingSessionFiles_AndAgainChangesNothing()
    {
        WriteProject("in/Plant");
        Write("in/Plant/Plant.edb/User.eod", "session");
        await ImportAsync(new ScanInput(Path.Combine(_locations.Root, "in"), BundleProjects: true));

        CallToolResult first = await BuildAsync("1.0.0");
        _time.Advance(TimeSpan.FromHours(1));
        CallToolResult again = await BuildAsync("1.0.0");

        ToolResultAssert.Succeeded(first);
        JsonElement data = first.StructuredContent!.Value;
        Assert.Equal(("installed", "packs/acme.panels/1.0.0"), (data.GetProperty("outcome").GetString(), data.GetProperty("location").GetString()));
        Assert.Equal(data.GetProperty("digest").GetString(), PackDigest.Compute(File.ReadAllBytes(Path.Combine(Installed, PackManifest.FileName))));
        Assert.Equal("1.0.0", PackJson.ReadManifest(File.ReadAllBytes(Path.Combine(Installed, PackManifest.FileName))).Version);
        Assert.True(File.GetAttributes(Path.Combine(Installed, PackManifest.FileName)).HasFlag(FileAttributes.ReadOnly));
        Assert.True(File.Exists(Path.Combine(Installed, "resources", "projects", "in", "Plant", "Plant.edb", "User.eod")));
        Assert.Equal(("already_installed", data.GetProperty("digest").GetString()), (again.StructuredContent!.Value.GetProperty("outcome").GetString(), again.StructuredContent!.Value.GetProperty("digest").GetString()));
        Assert.Equal(DevPackStore.ManifestVersion, PackJson.ReadManifest(File.ReadAllBytes(Path.Combine(_locations.PacksDev, "acme.panels", PackManifest.FileName))).Version);
    }

    [Fact]
    public async Task Build_SameVersionOtherContent_IsAlreadyExists()
    {
        Write("in/guide.md", "# Guide");
        await ImportAsync(new ScanInput(Path.Combine(_locations.Root, "in")));
        await BuildAsync("1.0.0");
        Write("in2/notes.md", "# Notes");
        await ImportAsync(new ScanInput(Path.Combine(_locations.Root, "in2")));

        Assert.Equal(ErrorReason.AlreadyExists, Reason(await BuildAsync("1.0.0")));
        ToolResultAssert.Succeeded(await BuildAsync("1.1.0"));
    }

    [Fact]
    public async Task Build_PackEditedByHandOrChangedReference_IsPackInvalidAndInstallsNothing()
    {
        WriteProject("share/Refs/Plant");
        await ImportAsync(new ScanInput(Path.Combine(_locations.Root, "share", "Refs"), Root: new ResourceReference { Root = "company-refs", Path = "Refs" }));
        Write("share/Refs/Plant/Plant.edb/Page.eod", "changed pages");

        Assert.Equal(ErrorReason.PackInvalid, Reason(await BuildAsync("1.0.0")));
        Assert.False(Directory.Exists(Installed));
        Assert.Empty(Directory.GetDirectories(_locations.PacksDev, ".*"));
    }

    [Theory]
    [InlineData("acme.panels", "1.0", ErrorReason.InvalidArgument)]
    [InlineData("other", "1.0.0", ErrorReason.ResourceNotFound)]
    public async Task Build_BadArgumentsOrNoDevPack_AreErrors(string pack, string version, string reason)
    {
        Write("in/guide.md", "# Guide");
        await ImportAsync(new ScanInput(Path.Combine(_locations.Root, "in")));

        Assert.Equal(reason, Reason(await PackTools.PackBuildAsync(Packs, ReviewFixtures.Service(Packs), pack, version, Cancel)));
    }

    public void Dispose()
    {
        foreach (string file in Directory.EnumerateFiles(_locations.Root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_locations.Root, recursive: true);
    }

    private Task<CallToolResult> BuildAsync(string version) => PackTools.PackBuildAsync(Packs, ReviewFixtures.Service(Packs), "acme.panels", version, Cancel);

    private static string? Reason(CallToolResult result) =>
        result.IsError == true ? JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement.GetProperty("error").GetProperty("reason").GetString() : null;

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
