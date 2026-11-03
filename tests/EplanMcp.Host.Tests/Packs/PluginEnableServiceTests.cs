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
public sealed partial class PluginEnableServiceTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-enable-").FullName);
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 11, 2, 8, 30, 0, TimeSpan.Zero));
    private readonly FakeBridgeClient _bridge = new()
    {
        Status = new BridgeStatus([], new HandshakeResult { Eplan = new EplanInstanceInfo { ProcessId = 42 } }, null),
    };
    private PluginStateStore State => new(_locations);
    private PackLibrary Packs => new(_locations, HostConfig.Defaults(_locations), new AuditLog(_locations.Audit, _time), _time);
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EnableAndDisable_PreserveLibrariesAndInstalledFiles_AndAskOnceEach()
    {
        await InstallAsync("first", "1.0.0");
        await State.WriteAsync(new(1, [], [new("share", Path.Combine(_locations.Root, "library"))]), null, Token);
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);
        PluginEnableService service = Service(channel);

        Assert.True((await service.SetAsync("first", "1.0.0", true, null, Token)).Changed);
        Assert.False((await service.SetAsync("first", "1.0.0", true, null, Token)).Changed);
        Assert.Equal([new PluginIdentity("first", "1.0.0")], (await State.ReadAsync(Token)).State.Enabled);
        Assert.True((await service.SetAsync("first", "1.0.0", false, null, Token)).Changed);
        Assert.False((await service.SetAsync("first", "1.0.0", false, null, Token)).Changed);

        Assert.Equal(2, channel.Questions.Count);
        Assert.Empty((await State.ReadAsync(Token)).State.Enabled);
        Assert.Single((await State.ReadAsync(Token)).State.Libraries);
        Assert.NotNull(Packs.Installed.InstalledDigest("first", "1.0.0"));
    }

    [Fact]
    public async Task Replacement_PreservesEnableOrder_AndOldVersionDisableDoesNothing()
    {
        await InstallAsync("first", "1.0.0");
        await InstallAsync("first", "2.0.0");
        await InstallAsync("second", "1.0.0");
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);
        PluginEnableService service = Service(channel);
        await service.SetAsync("first", "1.0.0", true, null, Token);
        await service.SetAsync("second", "1.0.0", true, null, Token);
        await service.SetAsync("first", "2.0.0", true, null, Token);
        Assert.Equal([new PluginIdentity("first", "2.0.0"), new PluginIdentity("second", "1.0.0")], (await State.ReadAsync(Token)).State.Enabled);
        Assert.False((await service.SetAsync("first", "1.0.0", false, null, Token)).Changed);
        Assert.Equal(3, channel.Questions.Count);
    }

    [Fact]
    public async Task NotInstalled_IsRefusedWithoutApprovalOrStateWrite()
    {
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);
        CallToolResult result = await PluginEnableTools.SetAsync(Service(channel), "missing", "1.0.0", true, cancellationToken: Token);
        Assert.Equal(ErrorReason.ResourceNotFound, Reason(result));
        Assert.Empty(channel.Questions);
        Assert.Null((await State.ReadAsync(Token)).Revision);
    }

    [Theory]
    [InlineData("Denied", ErrorReason.ApprovalDenied)]
    [InlineData("Expired", ErrorReason.ApprovalExpired)]
    public async Task DeniedOrExpired_PreservesState(string decision, string reason)
    {
        await InstallAsync("first", "1.0.0");
        var channel = new FakeApprovalChannel("eplan_dialog", Enum.Parse<ApprovalDecision>(decision));
        CallToolResult result = await PluginEnableTools.SetAsync(Service(channel), "first", "1.0.0", true, cancellationToken: Token);
        Assert.Equal(reason, Reason(result));
        Assert.Single(channel.Questions);
        Assert.Null((await State.ReadAsync(Token)).Revision);
    }

    [Fact]
    public async Task Approval_UsesEplanDialogAndSkipsWorkbench()
    {
        await InstallAsync("first", "1.0.0");
        var workbench = new FakeApprovalChannel("workbench", ApprovalDecision.Granted);
        var eplan = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);
        await Service(workbench, eplan).SetAsync("first", "1.0.0", true, null, Token);
        Assert.Empty(workbench.Questions);
        Assert.Single(eplan.Questions);
    }

    [Fact]
    public async Task RevisionChangedDuringApproval_ReturnsRetryErrorAndPreservesConcurrentState()
    {
        await InstallAsync("first", "1.0.0");
        var concurrent = new PluginState(1, [], [new("share", Path.Combine(_locations.Root, "concurrent"))]);
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted)
        {
            Wait = () =>
            {
                Directory.CreateDirectory(_locations.State);
                File.WriteAllBytes(Path.Combine(_locations.State, "plugins.json"), PackJson.Serialize(concurrent));
            },
        };
        CallToolResult result = await PluginEnableTools.SetAsync(Service(channel), "first", "1.0.0", true, cancellationToken: Token);
        Assert.Equal(ErrorReason.PlanStale, Reason(result));
        Assert.Contains("retry", ((TextContentBlock)result.Content[0]).Text, StringComparison.Ordinal);
        Assert.Equal(concurrent.Libraries, (await State.ReadAsync(Token)).State.Libraries);
        Assert.Empty((await State.ReadAsync(Token)).State.Enabled);
    }

    [Fact]
    public async Task UnreadableState_IsWarningWithoutApprovalOrOverwrite()
    {
        Directory.CreateDirectory(_locations.State);
        File.WriteAllText(Path.Combine(_locations.State, "plugins.json"), "{broken");
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);
        PluginEnableResult result = await Service(channel).SetAsync("first", "1.0.0", true, null, Token);
        Assert.False(result.Changed);
        Assert.Null(result.Enabled);
        Assert.NotEmpty(result.Warnings);
        Assert.Empty(channel.Questions);
        Assert.Equal("{broken", File.ReadAllText(Path.Combine(_locations.State, "plugins.json")));
    }

    [Fact]
    public async Task AuditUnavailable_DoesNotAskOrWriteState()
    {
        await InstallAsync("first", "1.0.0");
        Directory.CreateDirectory(Path.GetDirectoryName(_locations.Audit)!);
        File.WriteAllText(_locations.Audit, "blocked");
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);
        CallToolResult result = await PluginEnableTools.SetAsync(Service(channel), "first", "1.0.0", true, cancellationToken: Token);
        Assert.Equal(ErrorReason.AuditUnavailable, Reason(result));
        Assert.Empty(channel.Questions);
        Assert.Null((await State.ReadAsync(Token)).Revision);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RepeatedEnable_WithMissingOrUnreadableManifest_ReportsWarning(bool missing)
    {
        await InstallAsync("first", "1.0.0");
        await State.WriteAsync(new(1, [new("first", "1.0.0")], []), null, Token);
        string manifest = Path.Combine(Packs.Installed.FolderOf("first", "1.0.0"), PackManifest.FileName);
        File.SetAttributes(manifest, FileAttributes.Normal);
        if (missing)
        { File.Delete(manifest); }
        else
        { File.WriteAllText(manifest, "{broken"); }
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);
        PluginEnableResult result = await Service(channel).SetAsync("first", "1.0.0", true, null, Token);
        Assert.False(result.Changed);
        Assert.NotEmpty(result.Warnings);
        Assert.Empty(channel.Questions);
    }

    private PluginEnableService Service(params FakeApprovalChannel[] channels) => new(Packs, State,
        new ApprovalService(channels, HostConfig.Defaults(_locations), _time, NullLogger<ApprovalService>.Instance), _bridge);

    private async Task InstallAsync(string id, string version)
    {
        string folder = Path.Combine(_locations.Root, "stage", id, version);
        Directory.CreateDirectory(folder);
        var manifest = new PackManifest
        {
            SchemaVersion = 3,
            Id = id,
            Name = id,
            Version = version,
            Kind = PackKind.Standard,
            Provides = ["symbol-defaults"],
            Standards = ["IEC"],
            Compatibility = new(),
            Resources = [],
            Provenance = new() { CreatedBy = "test", CreatedAt = _time.GetUtcNow() },
        };
        File.WriteAllBytes(Path.Combine(folder, PackManifest.FileName), PackJson.Serialize(manifest));
        Assert.Equal(PackInstallOutcome.Installed, (await Packs.Installed.InstallAsync(folder, Token)).Outcome);
    }

    private static string? Reason(CallToolResult result) => JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement.GetProperty("error").GetProperty("reason").GetString();

    public void Dispose()
    {
        foreach (string file in Directory.EnumerateFiles(_locations.Root, "*", SearchOption.AllDirectories))
        { File.SetAttributes(file, FileAttributes.Normal); }
        Directory.Delete(_locations.Root, true);
    }
}
