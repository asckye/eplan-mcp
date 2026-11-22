using System.Diagnostics;
using EplanMcp.Workbench.Vm;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Tests.Vm;

[Trait("Size", "Small")]
[Collection(WpfTestGroup.Name)]
public sealed class VmServiceTests
{
    [Fact]
    public async Task Start_ReportsStartingThenHostBoundEndpoint_AndStopRestoresEditing()
    {
        var fake = new FakeVm();
        var launcher = new FakeVmLauncher();
        using VmView view = fake.Create(launcher: launcher);
        Assert.Empty(launcher.Starts);
        Task running = fake.RunAsync(view, "start");
        Assert.True(view.Running);
        Assert.True(view.Starting);
        Assert.Equal(Loc.Current["Vm.Starting"], view.ServiceStatus);
        Assert.Equal((fake.Host.Executable, "192.168.56.2", "8766", (int?)null), Assert.Single(launcher.Starts));
        foreach (string action in new[] { "start", "settings" })
        {
            Assert.Equal(Loc.Current["Vm.StopFirst"], view.DisabledReason(action));
        }

        foreach (string action in new[] { "allow", "remove", "refresh", "stop" })
        {
            Assert.Empty(view.DisabledReason(action));
        }

        Task listening = WhenListening(view);
        launcher.Child.Listening.SetResult(new VmEndpoint("192.168.56.2", 8766));
        await listening;
        Assert.Contains("http://192.168.56.2:8766/mcp", view.ServiceStatus, StringComparison.Ordinal);
        Assert.False(view.Starting);
        await fake.RunAsync(view, "allow");
        Assert.Single(fake.Elevated);
        await fake.RunAsync(view, "stop");
        await running;
        Assert.True(launcher.Child.Stopped);
        Assert.False(view.Running);
        Assert.Equal(Loc.Current["Vm.Stopped"], view.ServiceStatus);
        Assert.Empty(view.DisabledReason("settings"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostExit_ShowsHostErrorAndRedactsLongTokens(bool afterListening)
    {
        var fake = new FakeVm();
        var launcher = new FakeVmLauncher();
        using VmView view = fake.Create(launcher: launcher);
        Task running = fake.RunAsync(view, "start");
        if (afterListening)
        {
            Task listening = WhenListening(view);
            launcher.Child.Listening.SetResult(new VmEndpoint(view.Address, 8766));
            await listening;
        }

        string token = new('Z', 43);
        launcher.Child.Exit.SetResult($"Address already in use. {token}\u202E");
        await running;
        Assert.True(view.ServiceFailed);
        Assert.False(view.Running);
        Assert.Contains("Address already in use.", view.ServiceStatus, StringComparison.Ordinal);
        Assert.DoesNotContain(token, view.ServiceStatus, StringComparison.Ordinal);
        Assert.DoesNotContain('\u202E', view.ServiceStatus);
    }

    [Fact]
    public async Task Start_NeedsNoEplanInstance_AndNeverPinsOne()
    {
        // ADR-0067: the service starts with no EPLAN running and is bound to none; the AI starts or selects EPLAN.
        var fake = new FakeVm();
        var launcher = new FakeVmLauncher();
        using VmView view = fake.Create(launcher: launcher);
        Assert.Empty(view.DisabledReason("start"));

        Task running = fake.RunAsync(view, "start");

        Assert.Null(Assert.Single(launcher.Starts).Process);
        Assert.DoesNotContain(fake.Host.Runs, r => r.Arguments[0] == "vm-instances");
        await fake.RunAsync(view, "stop");
        await running;
    }

    [Fact]
    public async Task Copy_PutsTheMcpAddressOnTheClipboard()
    {
        var fake = new FakeVm();
        using VmView view = fake.Create();

        await fake.RunAsync(view, "copy");

        Assert.Equal(["http://192.168.56.2:8766/mcp"], fake.Clipboard);
        Assert.Equal(Loc.Current["Vm.Copied"], view.Message);
    }
    [Theory]
    [InlineData(null)]
    [InlineData(200)]
    public void ProcessStart_UsesValidatedArgumentListWithoutShellWindowOrSecrets(int? pid)
    {
        ProcessStartInfo start = VmProcess.StartInfo(@"C:\Program Files\EplanMcp\eplanmcp.exe", "192.168.56.2", "8766", pid);
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
        Assert.False(start.RedirectStandardInput);
        string[] expected = ["serve", "--http", "--address", "192.168.56.2", "--port", "8766", .. pid is null ? Array.Empty<string>() : ["--process", "200"]];
        Assert.Equal(expected, start.ArgumentList);
    }

    [Fact]
    public async Task ListeningTimeout_StopsChildAndExplainsRetry()
    {
        var fake = new FakeVm();
        var launcher = new FakeVmLauncher();
        using VmView view = fake.Create(launcher: launcher);
        Task running = fake.RunAsync(view, "start");
        launcher.Child.Listening.SetCanceled(TestContext.Current.CancellationToken);
        await running;
        Assert.True(launcher.Child.Stopped);
        Assert.True(view.ServiceFailed);
        Assert.Contains(Loc.Current["Vm.StartTimeout"], view.ServiceStatus, StringComparison.Ordinal);
    }

    private static Task WhenListening(VmView view)
    {
        var seen = new TaskCompletionSource();
        view.PropertyChanged += (_, _) => { if (view.Running && !view.Starting) { seen.TrySetResult(); } };
        return seen.Task.WaitAsync(TestContext.Current.CancellationToken);
    }
}
