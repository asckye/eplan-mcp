using System.ComponentModel;
using EplanMcp.Contracts;
using EplanMcp.Host.Addin;

namespace EplanMcp.Host.Tests.Addin;

/// <summary>
/// One start of EPLAN for a command, against the fake EPLAN of the live check: waiting for the Bridge of the process
/// it started, the handshake, and closing without ever forcing, on a clock that steps (<see cref="SteppingTime"/>). No
/// EPLAN.exe is started. Medium.
/// </summary>
[Trait("Size", "Medium")]
public sealed class EplanLauncherTests
{
    private const string Eplan = @"C:\Program Files\EPLAN\Platform\2026.0.3\Bin\EPLAN.exe";
    private const string Dll = @"C:\Users\工程师\EplanMcp\app\bridge\net481\EplanMcp.Bridge.Eplan.dll";
    private const string Register = $"/Variant:\"Electric P8\" EplApiModuleAction /register:\"{Dll}\"";

    private static readonly AddinTimings _timings = new(TimeSpan.FromMilliseconds(200), TimeSpan.Zero, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1));
    private static readonly AddinResult _result = new(Eplan, "2026.0.3.25421", "Electric P8", AddinOutcome.Refused);

    private readonly FakeEplan _eplan = new();
    private readonly List<HandshakeResult?> _loaded = [];

    private EplanLauncher Launcher => new(_eplan, _eplan, new SteppingTime(DateTimeOffset.UnixEpoch), _timings);

    [Fact]
    public async Task OnceItsBridgeIsUp_HandsOverTheHandshakeAndClosesEplan()
    {
        Launch launch = await Launcher.RunAsync(_result, Register, TimeSpan.Zero, _loaded.Add);

        Assert.Equal(($"start {Eplan} {Register}", "close 4000"), (_eplan.Log[0], _eplan.Log[1]));
        Assert.Equal((null, true, 4000, 0), (launch.Problem, launch.Closed, launch.Result.ProcessId, launch.Result.Notes.Count));
        Assert.Equal(Dll, Assert.Single(_loaded)?.AddinPath);
        Assert.Same(_loaded[0], launch.Handshake);
        Assert.True(_eplan.Instances.Single().Disposed);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(int.MaxValue, false)]
    public async Task AHandshakeThatFails_IsTriedAgainBeforeGivingUp(int failures, bool answers)
    {
        _eplan.HandshakeFailures = failures;

        Launch launch = await Launcher.RunAsync(_result, Register, TimeSpan.Zero, _loaded.Add);

        Assert.Equal((answers, (AddinIssue?)null, true), (launch.Handshake is not null, launch.Problem, launch.Closed));
        Assert.Equal(answers, Assert.Single(_loaded) is not null);
    }

    [Fact]
    public async Task AnEplanThatDoesNotReactAtOnce_IsAskedAgainUntilItCloses()
    {
        _eplan.IgnoresCloses = 3;

        Launch launch = await Launcher.RunAsync(_result, Register, TimeSpan.Zero, _loaded.Add);

        Assert.True(launch.Closed);
        Assert.Equal(4, _eplan.Log.Count(l => l == "close 4000"));
    }

    [Fact]
    public async Task EveryCloseRequest_ComesRightAfterARefresh()
    {
        // Second live check: Process keeps the splash screen's handle, so a request without refreshing goes nowhere.
        _eplan.IgnoresCloses = 2;

        await Launcher.RunAsync(_result, Register, TimeSpan.Zero, _loaded.Add);

        Assert.Equal(["refresh", "close", "refresh", "close", "refresh", "close"], _eplan.Instances.Single().Calls);
    }

    [Fact]
    public void TheFake_LikeARealProcess_IgnoresARequestSentWithoutRefreshing()
    {
        IStartedEplan started = _eplan.Start(Eplan, Register);

        started.CloseMainWindow();
        Assert.False(started.HasExited);

        started.Refresh();
        started.CloseMainWindow();
        Assert.True(started.HasExited);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task AnEplanThatDoesNotClose_IsLeftOpenWithANote(bool hasWindow, bool closesWhenAsked)
    {
        _eplan.HasWindow = hasWindow;
        _eplan.ClosesWhenAsked = closesWhenAsked;

        Launch launch = await Launcher.RunAsync(_result, Register, TimeSpan.Zero, _loaded.Add);

        Assert.Equal((false, AddinNote.CloseEplan, (AddinIssue?)null), (launch.Closed, launch.Result.Notes.Single(), launch.Problem));
        Assert.False(_eplan.Instances.Single().Exited);
        Assert.True(_eplan.Log.Count(l => l == "close 4000") > 1);
        Assert.Single(_loaded);
    }

    [Fact]
    public async Task AnEplanStillStartingAfterTheWait_IsLeftAloneAndNotLoaded()
    {
        _eplan.PipeAfterLooks = null;

        Launch launch = await Launcher.RunAsync(_result, Register, TimeSpan.Zero, _loaded.Add);

        Assert.Equal((new AddinIssue(AddinProblem.NotLoaded, "4000"), false), (launch.Problem, launch.Closed));
        Assert.DoesNotContain(_eplan.Log, l => l.StartsWith("close", StringComparison.Ordinal));
        Assert.Empty(_loaded);
    }

    [Fact]
    public async Task AnEplanThatExitsFirst_HasExited()
    {
        _eplan.PipeAfterLooks = null;
        _eplan.ExitsBeforePipe = true;

        Launch launch = await Launcher.RunAsync(_result, Register, TimeSpan.Zero, _loaded.Add);

        Assert.Equal((new AddinIssue(AddinProblem.EplanExited, "4000"), true), (launch.Problem, launch.Closed));
        Assert.Empty(_loaded);
    }

    [Fact]
    public async Task AnEplanThatCannotStart_SaysWhy()
    {
        _eplan.StartFails = new Win32Exception(2, "The system cannot find the file specified.");

        Launch launch = await Launcher.RunAsync(_result, Register, TimeSpan.Zero, _loaded.Add);

        Assert.Equal((new AddinIssue(AddinProblem.StartFailed, "The system cannot find the file specified."), (int?)null), (launch.Problem, launch.Result.ProcessId));
    }
}
