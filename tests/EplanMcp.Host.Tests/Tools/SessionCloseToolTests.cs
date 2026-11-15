using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Addin;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Addin;
using EplanMcp.Host.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>eplan_session_close and its checks (ADR-0058) against <see cref="FakeEplan"/>; nothing real is closed.</summary>
[Trait("Size", "Small")]
public sealed class SessionCloseToolTests : IDisposable
{
    private static readonly AddinTimings _timings = new(TimeSpan.FromMilliseconds(200), TimeSpan.Zero, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1));

    private readonly WriteToolHarness _harness = new();
    private readonly FakeEplan _eplan = new();
    private readonly SteppingTime _clock = new(new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero));
    private readonly TaskRegistry _tasks = new(TimeProvider.System);
    private readonly int _pid;

    public SessionCloseToolTests()
    {
        _eplan.Registered["Electric P8"] = @"C:\app\bridge\EplanMcp.Bridge.Eplan.dll";
        _pid = _eplan.Start("EPLAN.exe", "/Variant:\"Electric P8\"").Id;
        _harness.Bridge.Status = new BridgeStatus(
            [new BridgeEndpoint("p", _pid), new BridgeEndpoint("q", 77)],
            new HandshakeResult { ProtocolVersion = 1, BridgeVersion = "b", Eplan = new EplanInstanceInfo { ProcessId = _pid, Version = "2026.0.3" } },
            null);
        _harness.Bridge.Projects.Add(new OpenProject { Name = "Line1", Path = _harness.ProjectPath });
    }

    public void Dispose()
    {
        _tasks.Dispose();
        _harness.Dispose();
    }

    [Fact]
    public async Task DryRun_ReportsTheChecks_AndClosesNothing()
    {
        CallToolResult result = await CloseAsync(_pid);

        var data = WriteToolHarness.Structured(result);
        Assert.Equal((0, 0, 0, "not_started"), (data.GetProperty("writesInFlight").GetInt32(), data.GetProperty("planTasks").GetInt32(),
            data.GetProperty("suspendedWrites").GetInt32(), data.GetProperty("execution").GetString()));
        Assert.True(Assert.Single(data.GetProperty("projects").EnumerateArray().ToList()).GetProperty("inScratch").GetBoolean());
        Assert.DoesNotContain(_eplan.Log, line => line.StartsWith("close", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Closes_OnlyWhenAskedTo_AndReportsItCommitted()
    {
        CallToolResult result = await CloseAsync(_pid, dryRun: false);

        Assert.Equal("committed", WriteToolHarness.Structured(result).GetProperty("execution").GetString());
        Assert.DoesNotContain(_pid, _eplan.Running());
    }

    [Fact]
    public async Task AProjectOutsideScratch_IsRefusedByName_WithoutItsPath()
    {
        string outside = Path.Combine(_harness.Locations.Root, "customer", "Plant7");
        _harness.Bridge.Projects.Add(new OpenProject { Name = "Plant7", Path = outside });

        var error = WriteToolHarness.Error(await CloseAsync(_pid, dryRun: false));

        Assert.Equal(ErrorReason.PolicyDenied, error.GetProperty("reason").GetString());
        string message = error.GetProperty("message").GetString()!;
        Assert.Contains("Plant7", message, StringComparison.Ordinal);
        Assert.DoesNotContain(outside, message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(_pid, _eplan.Running());
    }

    [Fact]
    public async Task ASuspendedWrite_IsRefused()
    {
        SuspensionStore.For(_harness.Locations).Suspend(_harness.ProjectPath, _pid, DateTimeOffset.UnixEpoch, "eplan_page_update", "r1",
            DateTimeOffset.UnixEpoch, SuspensionReason.OutcomeUnknown, ["page"]);

        var error = WriteToolHarness.Error(await CloseAsync(_pid, dryRun: false));

        Assert.Equal(ErrorReason.WritesSuspended, error.GetProperty("reason").GetString());
        Assert.Contains(_pid, _eplan.Running());
    }

    [Fact]
    public async Task ASuspensionOfAProjectNotOpen_DoesNotBlockClosing()
    {
        SuspensionStore.For(_harness.Locations).Suspend(Path.Combine(_harness.Locations.DefaultScratch, "Closed"), _pid, DateTimeOffset.UnixEpoch,
            "eplan_page_update", "r1", DateTimeOffset.UnixEpoch, SuspensionReason.OutcomeUnknown, ["page"]);

        var data = WriteToolHarness.Structured(await CloseAsync(_pid));

        Assert.Equal(0, data.GetProperty("suspendedWrites").GetInt32());
    }

    [Fact]
    public async Task AnotherProcess_ThanTheConnectedOne_IsRefused()
    {
        var error = WriteToolHarness.Error(await CloseAsync(77, dryRun: false));

        Assert.Equal(ErrorReason.SessionNotSelected, error.GetProperty("reason").GetString());
        Assert.Contains("77", error.GetProperty("metadata").GetProperty("processes").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEplanThatStaysOpen_IsATimeout_AndIsNotForced()
    {
        _eplan.ClosesWhenAsked = false;

        var error = WriteToolHarness.Error(await CloseAsync(_pid, dryRun: false));

        Assert.Equal(ErrorReason.Timeout, error.GetProperty("reason").GetString());
        Assert.Contains(_pid, _eplan.Running());
    }

    [Fact]
    public async Task APinnedHttpHost_CannotClose()
    {
        var error = WriteToolHarness.Error(await CloseAsync(_pid, dryRun: false, pinned: true));

        Assert.Equal(ErrorReason.CapabilityMissing, error.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Restart_DryRun_OnlyChecks()
    {
        var data = WriteToolHarness.Structured(await RestartAsync(dryRun: true));

        Assert.Equal("not_started", data.GetProperty("close").GetProperty("execution").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("start").ValueKind);
        Assert.Contains(_pid, _eplan.Running());
    }

    [Fact]
    public async Task Restart_StartsTheVariantTheClosedEplanRan()
    {
        InstallEplan("Electric P8", "Pro Panel");
        _eplan.Registered["Pro Panel"] = @"C:\app\bridge\EplanMcp.Bridge.Eplan.dll";
        _harness.Bridge.Status.Handshake!.Eplan.Variant = "Pro Panel";

        var data = WriteToolHarness.Structured(await RestartAsync(dryRun: false));

        Assert.Equal(("committed", "Pro Panel", "b"), (data.GetProperty("close").GetProperty("execution").GetString(),
            data.GetProperty("start").GetProperty("variant").GetString(), data.GetProperty("bridgeVersionBefore").GetString()));
        Assert.DoesNotContain(_pid, _eplan.Running());
    }

    [Fact]
    public async Task Restart_SaysEplanIsClosed_WhenStartingAgainFails()
    {
        var error = WriteToolHarness.Error(await RestartAsync(dryRun: false));

        Assert.Equal(ErrorReason.CapabilityMissing, error.GetProperty("reason").GetString());
        Assert.StartsWith("EPLAN is closed and was not started again", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    private string Root => _harness.Locations.Root;

    private EplanLifecycle Lifecycle(bool pinned = false) => new(
        new AddinMachine(Path.Combine(Root, "pf"), Path.Combine(Root, "pub"), Path.Combine(Root, "prof"), Path.Combine(Root, "app"), _ => "2026.0.3.1"),
        _eplan, _eplan, _clock, _timings, pinned);

    private HostConfig Config => new([_harness.Locations.DefaultScratch], OutsideScratchWrites.Approve);

    private Task<CallToolResult> CloseAsync(int processId, bool dryRun = true, bool pinned = false) =>
        SessionLifecycleTools.CloseAsync(Lifecycle(pinned), _harness.Bridge, _harness.Pipeline, _tasks, SuspensionStore.For(_harness.Locations),
            Config, processId, dryRun, TestContext.Current.CancellationToken);

    private Task<CallToolResult> RestartAsync(bool dryRun) =>
        SessionLifecycleTools.RestartAsync(Lifecycle(), _harness.Bridge, _harness.Pipeline, _tasks, SuspensionStore.For(_harness.Locations),
            Config, new Progress<ProgressNotificationValue>(), _pid, dryRun, TestContext.Current.CancellationToken);

    private void InstallEplan(params string[] variants)
    {
        string exe = Path.Combine(Root, "pf", "EPLAN", "Platform", "2026.0.3", "Bin", "EPLAN.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, string.Empty);
        foreach (string variant in variants)
        {
            string bin = Path.Combine(Root, "pf", "EPLAN", variant, "2026.0.3", "Bin");
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(bin, "W3u.exe"), string.Empty);
        }
    }
}
