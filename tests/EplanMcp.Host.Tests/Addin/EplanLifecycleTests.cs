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
        EplanStartException running = await Assert.ThrowsAsync<EplanStartException>(() => Lifecycle().StartAsync(null, null, TestContext.Current.CancellationToken));
        _eplan.Others.Clear();
        EplanStartException unknown = await Assert.ThrowsAsync<EplanStartException>(() => Lifecycle().StartAsync("Harness proD", null, TestContext.Current.CancellationToken));

        Assert.Equal((ErrorReason.AlreadyExists, "77"), (running.Reason, running.Metadata["processes"]));
        Assert.Equal(ErrorReason.InvalidArgument, unknown.Reason);
        Assert.Contains("Electric P8, Pro Panel", unknown.Message, StringComparison.Ordinal);
        Assert.Empty(_eplan.Log);
    }

    [Fact]
    public async Task Start_ReportsAnEplanThatExits_AndOneWhoseAddinNeverAnswers_LeavingItRunning()
    {
        _eplan.ExitsBeforePipe = true;
        EplanStartException exited = await Assert.ThrowsAsync<EplanStartException>(() => Lifecycle().StartAsync(null, null, TestContext.Current.CancellationToken));
        _eplan.ExitsBeforePipe = false;
        _eplan.PipeAfterLooks = null;
        EplanStartException silent = await Assert.ThrowsAsync<EplanStartException>(() => Lifecycle().StartAsync(null, null, TestContext.Current.CancellationToken));

        Assert.Equal(ErrorReason.EplanNotConnected, exited.Reason);
        Assert.Equal(ErrorReason.Timeout, silent.Reason);
        Assert.Contains("left running", silent.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_eplan.Log, line => line.StartsWith("close", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Start_IsUnavailableToAPinnedHttpHost_AndWithoutASupportedEplan()
    {
        EplanStartException pinned = await Assert.ThrowsAsync<EplanStartException>(() => Lifecycle(pinned: true).StartAsync(null, null, TestContext.Current.CancellationToken));
        Directory.Delete(Path.Combine(ProgramFiles, "EPLAN"), recursive: true);
        EplanStartException missing = await Assert.ThrowsAsync<EplanStartException>(() => Lifecycle().StartAsync(null, null, TestContext.Current.CancellationToken));

        Assert.Equal((ErrorReason.CapabilityMissing, ErrorReason.CapabilityMissing), (pinned.Reason, missing.Reason));
        Assert.Empty(_eplan.Log);
    }

    private EplanLifecycle Lifecycle(bool pinned = false) => new(
        new AddinMachine(ProgramFiles, Path.Combine(_root, "Public"), Path.Combine(_root, "Profile"), Path.Combine(_root, "App"),
            exe => Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(exe))) + ".1"),
        _eplan, _eplan, _clock, _timings, pinned);

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
