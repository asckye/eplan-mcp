using System.Text.Json;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;
using EplanMcp.Packs.Scanning;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Packs;

/// <summary>eplan_pack_list on packs written into a temporary data folder (docs/tools/eplan_pack_list.md).</summary>
[Trait("Size", "Medium")]
public sealed class PackListToolTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-pack-list-").FullName);
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 10, 21, 8, 30, 0, TimeSpan.Zero));

    private PackLibrary Packs => new(_locations, HostConfig.Defaults(_locations), new AuditLog(_locations.Audit, _time), _time);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task List_DevAndInstalledVersions_AreOneEntryWithTheirLocks()
    {
        await DevPackAsync("acme.panels");
        await InstallAsync("acme.panels", "1.0.0");
        await InstallAsync("acme.panels", "1.1.0");
        await Packs.Workspaces.LockPackAsync(@"D:\PROJECTS\LINE1", "acme.panels", "1.0.0", Cancel);

        CallToolResult result = await PackTools.PackListAsync(Packs, cancellationToken: Cancel);

        ToolResultAssert.Succeeded(result);
        JsonElement pack = Assert.Single(result.StructuredContent!.Value.GetProperty("packs").EnumerateArray());
        Assert.Equal(("acme.panels", "ACME panels", 1), (pack.GetProperty("id").GetString(), pack.GetProperty("name").GetString(), pack.GetProperty("dev").GetProperty("resources").GetInt32()));
        Assert.Equal(["1.1.0", "1.0.0"], pack.GetProperty("installed").EnumerateArray().Select(v => v.GetProperty("version").GetString()));
        Assert.Equal([@"D:\PROJECTS\LINE1"], pack.GetProperty("installed")[1].GetProperty("lockedBy").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal("Listed 1 of 1 pack (1 in development, 2 installed versions).", ((TextContentBlock)result.Content[0]).Text);
    }

    [Fact]
    public async Task List_UnreadableManifest_IsListedWithAProblem()
    {
        Directory.CreateDirectory(Path.Combine(_locations.PacksDev, "broken"));
        File.WriteAllText(Path.Combine(_locations.PacksDev, "broken", PackManifest.FileName), "{ not json");
        await DevPackAsync("acme.panels");

        CallToolResult result = await PackTools.PackListAsync(Packs, name_contains: "BROK", cancellationToken: Cancel);

        JsonElement pack = Assert.Single(result.StructuredContent!.Value.GetProperty("packs").EnumerateArray());
        Assert.Equal(("broken", string.Empty, 1), (pack.GetProperty("id").GetString(), pack.GetProperty("name").GetString(), pack.GetProperty("dev").GetProperty("problems").GetInt32()));
    }

    [Fact]
    public async Task List_Empty_SaysHowToStart()
    {
        CallToolResult result = await PackTools.PackListAsync(Packs, cancellationToken: Cancel);

        Assert.Equal("No engineering packs yet; eplan_pack_import makes a development pack.", ((TextContentBlock)result.Content[0]).Text);
    }

    [Fact]
    public async Task List_CursorOfAnotherFilter_IsAnInvalidArgument()
    {
        await DevPackAsync("acme.panels");
        await DevPackAsync("acme.drives");
        string cursor = (await PackTools.PackListAsync(Packs, limit: 1, cancellationToken: Cancel)).StructuredContent!.Value.GetProperty("nextCursor").GetString()!;

        Assert.True((await PackTools.PackListAsync(Packs, name_contains: "acme", cursor: cursor, cancellationToken: Cancel)).IsError);
        Assert.Equal("acme.panels", (await PackTools.PackListAsync(Packs, limit: 1, cursor: cursor, cancellationToken: Cancel)).StructuredContent!.Value.GetProperty("packs")[0].GetProperty("id").GetString());
    }

    public void Dispose()
    {
        foreach (string file in Directory.EnumerateFiles(_locations.Root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_locations.Root, recursive: true);
    }

    private async Task DevPackAsync(string id)
    {
        string input = Path.Combine(_locations.Root, "in", id);
        Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "guide.md"), "# Guide");
        ScanResult scan = await ResourceScanner.ScanAsync([new ScanInput(input)], Cancel);
        await Packs.Dev.ImportAsync(id, "ACME panels", scan, false, "eplanmcp test", _time.GetUtcNow(), Cancel);
    }

    /// <summary>Installs <paramref name="version"/> of the development pack as the library does (the build tool comes later).</summary>
    private async Task InstallAsync(string id, string version)
    {
        string staging = Path.Combine(_locations.Root, "stage-" + version);
        foreach (string file in Directory.EnumerateFiles(Packs.Dev.FolderOf(id), "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(staging, Path.GetRelativePath(Packs.Dev.FolderOf(id), file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        PackManifest manifest = PackJson.ReadManifest(File.ReadAllBytes(Path.Combine(staging, PackManifest.FileName))) with { Version = version };
        byte[] json = PackJson.Serialize(manifest);
        File.WriteAllBytes(Path.Combine(staging, PackManifest.FileName), json);
        File.WriteAllBytes(Path.Combine(staging, PackManifest.LockFileName), PackJson.Serialize(PackLock.Create(new PackReference { Id = id, Version = version, Digest = PackDigest.Compute(json) }, [])));
        Assert.Equal(PackInstallOutcome.Installed, (await Packs.Installed.InstallAsync(staging, Cancel)).Outcome);
    }
}
