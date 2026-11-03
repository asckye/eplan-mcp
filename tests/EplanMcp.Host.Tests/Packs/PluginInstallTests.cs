using System.Globalization;
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
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Packs;

[Trait("Size", "Medium")]
public sealed class PluginInstallTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-plugin-install-").FullName;
    private readonly FakeBridgeClient _bridge = new()
    {
        Status = new BridgeStatus([], new HandshakeResult { Eplan = new EplanInstanceInfo { ProcessId = 42, Version = "2026.0.3.25421" } }, null),
    };

    private HostDataLocations Locations => new(Path.Combine(_root, "data"));
    private HostConfig Config => HostConfig.Defaults(Locations);
    private PackLibrary Packs => new(Locations, Config, new AuditLog(Locations.Audit, TimeProvider.System), TimeProvider.System);
    private PluginLibrarySource Source => new(Locations, new PluginStateStore(Locations)) { ApplicationDirectory = Path.Combine(_root, "app") };
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Builtin_InstallsWithoutApprovalOrEplan()
    {
        await WriteLibraryAsync("builtin", "builtin");
        _bridge.Status = new BridgeStatus([], null, null);
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Denied);

        JsonElement result = Data(await InstallAsync(channel, "builtin"));

        Assert.Equal("installed", result.GetProperty("installed").GetProperty("outcome").GetString());
        Assert.Empty(channel.Questions);
        Assert.True(File.Exists(Path.Combine(Installed("1.0.0"), PackManifest.FileName)));
        AssertAudit("builtin", "builtin", "started", "committed");
    }

    [Theory]
    [InlineData("local", "team")]
    [InlineData("local", "team library")]
    [InlineData("shared", "shared-team")]
    [InlineData("local", "builtin")]
    public async Task NonBuiltin_ConfirmsOnceWithSourceAndLeavesLibraryUnchanged(string type, string name)
    {
        string folder = await WriteLibraryAsync(type, name);
        var before = Directory.EnumerateFiles(folder).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);

        Data(await InstallAsync(channel, name));

        ApprovalQuestion asked = Assert.Single(channel.Questions);
        Assert.NotNull(asked.PackInstall);
        string summary = ApprovalTexts.For(asked, CultureInfo.GetCultureInfo("en-US")).Summary;
        Assert.Contains(name, summary, StringComparison.Ordinal);
        Assert.Contains(type, summary, StringComparison.Ordinal);
        Assert.Contains(folder, summary, StringComparison.Ordinal);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), Directory.EnumerateFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        foreach ((string? file, byte[] bytes) in before)
        { Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(folder, file!))); }
        AssertAudit(name, type, "started", "committed");
    }

    [Fact]
    public async Task Denied_LeavesNoInstalledVersionOrStaging()
    {
        await WriteLibraryAsync("local", "team");
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Denied);

        AssertReason(await InstallAsync(channel, "team"), ErrorReason.ApprovalDenied);

        Assert.Single(channel.Questions);
        Assert.False(Directory.Exists(Installed("1.0.0")));
        AssertNoStaging();
        AssertAudit("team", "local", "failed");
    }

    [Theory]
    [InlineData("builtin", "builtin")]
    [InlineData("local", "team")]
    public async Task IndexDigestMismatch_IsInvalidBeforeApproval(string type, string name)
    {
        string folder = await WriteLibraryAsync(type, name);
        PluginLibraryIndex index = PluginLibraryReader.Read(File.ReadAllBytes(Path.Combine(folder, PluginLibraryReader.FileName)));
        File.WriteAllBytes(Path.Combine(folder, PluginLibraryReader.FileName), PackJson.Serialize(index with
        { Plugins = [index.Plugins[0] with { Digest = new string('a', 64) }] }));
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);

        AssertReason(await InstallAsync(channel, name), ErrorReason.PackInvalid);

        Assert.Empty(channel.Questions);
        Assert.False(Directory.Exists(Installed("1.0.0")));
        AssertNoStaging();
    }

    [Fact]
    public async Task SameVersionAndDigest_IsNoOpWithoutSecondApproval()
    {
        await WriteLibraryAsync("local", "team");
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);
        Data(await InstallAsync(channel, "team"));
        byte[] before = File.ReadAllBytes(Path.Combine(Installed("1.0.0"), PackManifest.FileName));

        JsonElement again = Data(await InstallAsync(channel, "team"));

        Assert.Equal("already_installed", again.GetProperty("installed").GetProperty("outcome").GetString());
        Assert.Single(channel.Questions);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(Installed("1.0.0"), PackManifest.FileName)));
        AssertNoStaging();
    }

    [Fact]
    public async Task NewVersion_RequiresNewApprovalAndPreservesInstalledVersion()
    {
        await WriteLibraryAsync("local", "team");
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);
        Data(await InstallAsync(channel, "team"));
        byte[] before = File.ReadAllBytes(Path.Combine(Installed("1.0.0"), PackManifest.FileName));
        await WriteLibraryAsync("local", "team", "1.1.0");

        Data(await InstallAsync(channel, "team", "1.1.0"));

        Assert.Equal(["1.0.0", "1.1.0"], channel.Questions.Select(q => q.PackInstall!.Version));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(Installed("1.0.0"), PackManifest.FileName)));
        Assert.True(File.Exists(Path.Combine(Installed("1.1.0"), PackManifest.FileName)));
    }

    [Fact]
    public async Task SameVersionWithOtherDigest_IsRefusedAndNeverOverwritten()
    {
        await WriteLibraryAsync("local", "team");
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);
        Data(await InstallAsync(channel, "team"));
        byte[] before = File.ReadAllBytes(Path.Combine(Installed("1.0.0"), PackManifest.FileName));
        await WriteLibraryAsync("local", "team", title: "Changed content");

        AssertReason(await InstallAsync(channel, "team"), ErrorReason.AlreadyExists);

        Assert.Equal(before, File.ReadAllBytes(Path.Combine(Installed("1.0.0"), PackManifest.FileName)));
        AssertNoStaging();
    }

    [Fact]
    public async Task CorruptContainer_IsInvalidBeforeApproval()
    {
        string folder = await WriteLibraryAsync("local", "team");
        string archive = Directory.EnumerateFiles(folder, "*.eplanpack").Single();
        File.WriteAllText(archive, "invalid archive");
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);

        AssertReason(await InstallAsync(channel, "team"), ErrorReason.PackInvalid);

        Assert.Empty(channel.Questions);
        Assert.False(Directory.Exists(Installed("1.0.0")));
        AssertNoStaging();
    }

    [Fact]
    public async Task UnreadableSharedRegistration_IsWarningWithoutApproval()
    {
        await WriteLibraryAsync("shared", "team");
        File.WriteAllText(Path.Combine(Locations.State, "plugins.json"), "{invalid");
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);

        JsonElement result = Data(await InstallAsync(channel, "team"));

        Assert.Equal(JsonValueKind.Null, result.GetProperty("installed").ValueKind);
        Assert.NotEmpty(result.GetProperty("warnings").EnumerateArray());
        Assert.Empty(channel.Questions);
        Assert.False(Directory.Exists(Installed("1.0.0")));
        AssertNoStaging();
    }

    [Fact]
    public async Task MissingArchive_IsWarningWithoutApproval()
    {
        string folder = await WriteLibraryAsync("local", "team");
        File.Delete(Directory.EnumerateFiles(folder, "*.eplanpack").Single());
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);

        JsonElement result = Data(await InstallAsync(channel, "team"));

        Assert.Equal(JsonValueKind.Null, result.GetProperty("installed").ValueKind);
        Assert.NotEmpty(result.GetProperty("warnings").EnumerateArray());
        Assert.Empty(channel.Questions);
        Assert.False(Directory.Exists(Installed("1.0.0")));
        AssertNoStaging();
    }

    [Fact]
    public async Task SourceChangedDuringApproval_InstallsThePreviouslyCheckedCopy()
    {
        string folder = await WriteLibraryAsync("local", "team");
        string archive = Directory.EnumerateFiles(folder, "*.eplanpack").Single();
        string expected = PluginLibraryReader.Read(File.ReadAllBytes(Path.Combine(folder, PluginLibraryReader.FileName))).Plugins[0].Digest;
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted)
        {
            Wait = () => File.WriteAllText(archive, "replaced during approval"),
        };

        Data(await InstallAsync(channel, "team"));

        Assert.Equal(expected, Packs.Installed.InstalledDigest("iec-base", "1.0.0"));
        Assert.Single(channel.Questions);
    }

    [Theory]
    [InlineData("other-plugin", "1.0.0")]
    [InlineData("iec-base", "1.1.0")]
    public async Task IndexedIdentityDoesNotMatchArchive_IsInvalid(string id, string version)
    {
        string folder = await WriteLibraryAsync("local", "team");
        string file = Path.Combine(folder, PluginLibraryReader.FileName);
        PluginLibraryIndex index = PluginLibraryReader.Read(File.ReadAllBytes(file));
        File.WriteAllBytes(file, PackJson.Serialize(index with { Plugins = [index.Plugins[0] with { Id = id, Version = version }] }));
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);
        var installer = new PackInstaller(Packs,
            new ApprovalService([channel], Config, TimeProvider.System, NullLogger<ApprovalService>.Instance), _bridge);

        CallToolResult result = await PluginInstallTools.InstallAsync(Source, installer, "team", id, version, cancellationToken: Cancel);

        AssertReason(result, ErrorReason.PackInvalid);
        Assert.Empty(channel.Questions);
        Assert.False(Directory.Exists(Installed("1.0.0")));
        AssertNoStaging();
    }

    [Fact]
    public async Task UnavailableAudit_DoesNotInstall()
    {
        await WriteLibraryAsync("builtin", "builtin");
        Directory.CreateDirectory(Path.GetDirectoryName(Locations.Audit)!);
        File.WriteAllText(Locations.Audit, "not a directory");

        AssertReason(await InstallAsync(new("eplan_dialog", ApprovalDecision.Granted), "builtin"), ErrorReason.AuditUnavailable);

        Assert.False(Directory.Exists(Installed("1.0.0")));
        AssertNoStaging();
    }

    private Task<CallToolResult> InstallAsync(FakeApprovalChannel channel, string library, string version = "1.0.0") =>
        PluginInstallTools.InstallAsync(Source, new PackInstaller(Packs,
            new ApprovalService([channel], Config, TimeProvider.System, NullLogger<ApprovalService>.Instance), _bridge),
            library, "iec-base", version, cancellationToken: Cancel);

    private string Installed(string version) => Path.Combine(Locations.Packs, "iec-base", version);

    private async Task<string> WriteLibraryAsync(string type, string name, string version = "1.0.0", string title = "IEC defaults")
    {
        string folder = type switch
        {
            "builtin" => Path.Combine(_root, "app", "plugin-libraries", "builtin"),
            "shared" => Path.Combine(_root, "shared"),
            _ => Path.Combine(Locations.PluginLibraries, name),
        };
        Directory.CreateDirectory(folder);
        string source = Path.Combine(_root, "source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(source);
        byte[] manifest = PackJson.Serialize(new PackManifest
        {
            SchemaVersion = PackManifest.CurrentSchemaVersion,
            Id = "iec-base",
            Version = version,
            Name = title,
            Kind = PackKind.Standard,
            Compatibility = new(),
            Resources = [],
            Provenance = new() { CreatedBy = "test", CreatedAt = DateTimeOffset.UnixEpoch },
        });
        File.WriteAllBytes(Path.Combine(source, PackManifest.FileName), manifest);
        string file = Guid.NewGuid().ToString("N") + ".eplanpack";
        await PackArchive.ExportAsync(source, Path.Combine(folder, file), Cancel);
        File.WriteAllBytes(Path.Combine(folder, PluginLibraryReader.FileName), PackJson.Serialize(new PluginLibraryIndex
        {
            Name = name.Replace(' ', '-'),
            Plugins = [new() { Id = "iec-base", Version = version, Kind = PackKind.Standard,
                Digest = PackDigest.Compute(manifest), File = file, Title = title }],
        }));
        if (type == "shared")
        {
            var state = new PluginStateStore(Locations);
            string? revision = (await state.ReadAsync(Cancel)).Revision;
            await state.WriteAsync(new(1, [], [new(name, folder)]), revision, Cancel);
        }
        return folder;
    }

    private static JsonElement Data(CallToolResult result)
    {
        ToolResultAssert.Succeeded(result);
        return result.StructuredContent!.Value;
    }

    private static void AssertReason(CallToolResult result, string reason)
    {
        Assert.True(result.IsError);
        using JsonDocument document = JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text);
        Assert.Equal(reason, document.RootElement.GetProperty("error").GetProperty("reason").GetString());
    }

    private void AssertAudit(string library, string type, params string[] phases)
    {
        string[] lines = Directory.EnumerateFiles(Locations.Audit, AuditLog.FilePattern).SelectMany(File.ReadLines).ToArray();
        foreach (string phase in phases)
        {
            Assert.Contains(lines, line =>
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement entry = document.RootElement;
                return entry.GetProperty("kind").GetString() == "pack_change"
                    && entry.GetProperty("phase").GetString() == phase
                    && line.Contains(library, StringComparison.Ordinal)
                    && line.Contains(type, StringComparison.Ordinal);
            });
        }
    }

    private void AssertNoStaging()
    {
        if (Directory.Exists(Locations.Packs))
        { Assert.Empty(Directory.GetDirectories(Locations.Packs, ".*")); }
    }

    public void Dispose()
    {
        foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        { File.SetAttributes(file, FileAttributes.Normal); }
        Directory.Delete(_root, recursive: true);
    }
}
