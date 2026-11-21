using EplanMcp.Contracts;
using EplanMcp.Host.Addin;

namespace EplanMcp.Host.Tests.Addin;

/// <summary>eplan_session_start's service (ADR-0058) against a fake installation and <see cref="FakeEplan"/>; nothing starts.</summary>
[Trait("Size", "Small")]
public sealed class EplanLifecycleTests : IDisposable
{
    private static readonly AddinTimings _timings = new(TimeSpan.FromMilliseconds(200), TimeSpan.Zero, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1));

    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-lifecycle-").FullName;
    private readonly SteppingTime _clock = new(new DateTimeOffset(2026, 11, 14, 20, 0, 0, TimeSpan.Zero));
    private readonly FakeEplan _eplan = new();

    public EplanLifecycleTests()
    {
        InstallEplan("2026.0.3", "Electric P8", "Pro Panel");
        _eplan.Registered["Electric P8"] = @"C:\app\bridge\EplanMcp.Bridge.Eplan.dll";
        _eplan.Registered["Pro Panel"] = @"C:\app\bridge\EplanMcp.Bridge.Eplan.dll";
    }

    private string ProgramFiles => Path.Combine(_root, "Program Files");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Start_TakesElectricP8ByDefault_AndReturnsOnceTheBridgeAnswers()
    {
        EplanStart started = await Lifecycle().StartAsync(null, null, TestContext.Current.CancellationToken);

        Assert.Equal(("Electric P8", "2026.0.3.1", "0.1.0"), (started.Variant, started.Version, started.Handshake.BridgeVersion));
        Assert.Equal($"start {Exe("2026.0.3")} /Variant:\"Electric P8\"", Assert.Single(_eplan.Log));
        Assert.Equal(started.ProcessId, Assert.Single(_eplan.Running()));
    }

    [Fact]
    public async Task Start_TakesTheVariantAskedFor_IgnoringCase()
    {
        EplanStart started = await Lifecycle().StartAsync("pro panel", null, TestContext.Current.CancellationToken);

        Assert.Equal("Pro Panel", started.Variant);
    }

    [Fact]
    public async Task Start_RefusesWhileAnyEplanRuns_AndAnUnknownVariant()
    {
        _eplan.Others.Add(77);
        EplanLifecycleException running = await Assert.ThrowsAsync<EplanLifecycleException>(() => Lifecycle().StartAsync(null, null, TestContext.Current.CancellationToken));
        _eplan.Others.Clear();
        EplanLifecycleException unknown = await Assert.ThrowsAsync<EplanLifecycleException>(() => Lifecycle().StartAsync("Harness proD", null, TestContext.Current.CancellationToken));

        Assert.Equal((ErrorReason.AlreadyExists, "77"), (running.Reason, running.Metadata["processes"]));
        Assert.Equal(ErrorReason.InvalidArgument, unknown.Reason);
        Assert.Contains("Electric P8, Pro Panel", unknown.Message, StringComparison.Ordinal);
        Assert.Empty(_eplan.Log);
    }

    [Fact]
    public async Task Start_ReportsAnEplanThatExits_AndClosesOneWhoseAddinNeverAnswers_WithNothingInTheWay()
    {
        _eplan.ExitsBeforePipe = true;
        EplanLifecycleException exited = await Assert.ThrowsAsync<EplanLifecycleException>(() => Lifecycle().StartAsync(null, null, TestContext.Current.CancellationToken));
        _eplan.ExitsBeforePipe = false;
        _eplan.PipeAfterLooks = null;
        EplanLifecycleException silent = await Assert.ThrowsAsync<EplanLifecycleException>(() => Lifecycle().StartAsync(null, null, TestContext.Current.CancellationToken));

        // ADR-0063 decision 7: nothing in the way, so it is asked to close (never forced) and does not block the next start.
        Assert.Equal(ErrorReason.EplanNotConnected, exited.Reason);
        Assert.Equal(ErrorReason.Timeout, silent.Reason);
        Assert.Contains("closed that EPLAN", silent.Message, StringComparison.Ordinal);
        Assert.Contains(_eplan.Log, line => line.StartsWith("close", StringComparison.Ordinal));
        Assert.Empty(_eplan.Running());
    }

    [Fact]
    public async Task Start_ClosesTheSystemMessagesWindow_ThatKeepsTheAddinFromLoading()
    {
        _eplan.Dialogs.Add(EplanTestWindows.SystemMessages);
        _eplan.PipeAfterLooks = 20;

        EplanStart started = await Lifecycle().StartAsync(null, null, TestContext.Current.CancellationToken);

        Assert.Equal("system messages", Assert.Single(started.ClosedWindows).Name);
        Assert.Empty(_eplan.Dialogs);
        Assert.Contains(_eplan.Log, line => line.StartsWith("close window", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Start_BehindAnotherDialog_NamesIt_AndLeavesEplanRunning()
    {
        _eplan.Dialogs.Add(EplanTestWindows.SavePrompt);
        _eplan.PipeAfterLooks = null;

        EplanLifecycleException timedOut = await Assert.ThrowsAsync<EplanLifecycleException>(() => Lifecycle().StartAsync(null, null, TestContext.Current.CancellationToken));

        Assert.Equal(ErrorReason.Timeout, timedOut.Reason);
        Assert.Contains("left running", timedOut.Message, StringComparison.Ordinal);
        Assert.Contains(EplanTestWindows.SavePrompt.ToString(), timedOut.Metadata["windows"], StringComparison.Ordinal);
        Assert.DoesNotContain(_eplan.Log, line => line.StartsWith("close", StringComparison.Ordinal));
        Assert.Single(_eplan.Running());
    }

    [Fact]
    public async Task Start_WithTheWatchOnReport_LeavesTheSystemMessagesWindowOpen_AndNamesIt()
    {
        _eplan.Dialogs.Add(EplanTestWindows.SystemMessages);
        _eplan.PipeAfterLooks = null;

        EplanLifecycleException timedOut = await Assert.ThrowsAsync<EplanLifecycleException>(() => Lifecycle(closeHarmless: false).StartAsync(null, null, TestContext.Current.CancellationToken));

        Assert.Contains(EplanTestWindows.SystemMessages.ToString(), timedOut.Metadata["windows"], StringComparison.Ordinal);
        Assert.Single(_eplan.Dialogs);
        Assert.DoesNotContain(_eplan.Log, line => line.StartsWith("close", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Start_IsUnavailableToAPinnedHttpHost_AndWithoutASupportedEplan()
    {
        EplanLifecycleException pinned = await Assert.ThrowsAsync<EplanLifecycleException>(() => Lifecycle(pinned: true).StartAsync(null, null, TestContext.Current.CancellationToken));
        Directory.Delete(Path.Combine(ProgramFiles, "EPLAN"), recursive: true);
        EplanLifecycleException missing = await Assert.ThrowsAsync<EplanLifecycleException>(() => Lifecycle().StartAsync(null, null, TestContext.Current.CancellationToken));

        Assert.Equal((ErrorReason.CapabilityMissing, ErrorReason.CapabilityMissing), (pinned.Reason, missing.Reason));
        Assert.Empty(_eplan.Log);
    }

    private EplanLifecycle Lifecycle(bool pinned = false, bool closeHarmless = true) => new(
        new AddinMachine(ProgramFiles, Path.Combine(_root, "Public"), Path.Combine(_root, "Profile"), Path.Combine(_root, "App"),
            exe => Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(exe))) + ".1"),
        _eplan, _eplan, _clock, _timings, pinned, closeHarmless);

    private string Exe(string folder) => Path.Combine(ProgramFiles, "EPLAN", "Platform", folder, "Bin", "EPLAN.exe");

    private void InstallEplan(string folder, params string[] variants)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Exe(folder))!);
        File.WriteAllText(Exe(folder), string.Empty);
        foreach (string variant in variants)
        {
            string bin = Path.Combine(ProgramFiles, "EPLAN", variant, folder, "Bin");
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(bin, "W3u.exe"), string.Empty);
        }
    }
}
