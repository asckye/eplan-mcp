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
        var fake = new FakeVm { StoredKey = FakeVm.Key };
        var launcher = new FakeVmLauncher();
        using VmView view = fake.Create(launcher: launcher);
        await LoadAsync(fake, view);
        Assert.Empty(launcher.Starts);
        Task running = fake.RunAsync(view, "start");
        Assert.True(view.Running);
        Assert.True(view.Starting);
        Assert.Equal(Loc.Current["Vm.Starting"], view.ServiceStatus);
        Assert.Equal((fake.Host.Executable, "192.168.56.2", "8766", (int?)null), Assert.Single(launcher.Starts));
        foreach (string action in new[] { "start", "create", "delete", "settings", "instances" })
        {
            Assert.Equal(Loc.Current["Vm.StopFirst"], view.DisabledReason(action));
        }
        foreach (string action in new[] { "copy", "show", "allow", "remove", "refresh", "stop" })
        {
            Assert.Empty(view.DisabledReason(action));
        }
        await fake.RunAsync(view, "create");
        await fake.RunAsync(view, "delete");
        Assert.Empty(fake.Questions);
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
        Assert.Empty(view.DisabledReason("create"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostExit_ShowsHostErrorAndRedactsCurrentAndOtherKeys(bool afterListening)
    {
        var fake = new FakeVm { StoredKey = FakeVm.Key };
        var launcher = new FakeVmLauncher();
        using VmView view = fake.Create(launcher: launcher);
        await LoadAsync(fake, view);
        Task running = fake.RunAsync(view, "start");
        if (afterListening)
        {
            Task listening = WhenListening(view);
            launcher.Child.Listening.SetResult(new VmEndpoint(view.Address, 8766));
            await listening;
        }
        string otherKey = new('Z', 43);
        launcher.Child.Exit.SetResult($"Address already in use. {FakeVm.Key} {otherKey}\u202E");
        await running;
        Assert.True(view.ServiceFailed);
        Assert.False(view.Running);
        Assert.Contains("Address already in use.", view.ServiceStatus, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeVm.Key, view.ServiceStatus, StringComparison.Ordinal);
        Assert.DoesNotContain(otherKey, view.ServiceStatus, StringComparison.Ordinal);
        Assert.DoesNotContain('\u202E', view.ServiceStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkbenchExit_StopsStartingOrListeningChild_AndNeverRestarts(bool afterListening)
    {
        var fake = new FakeVm { StoredKey = FakeVm.Key };
        var launcher = new FakeVmLauncher();
        VmView view = fake.Create(launcher: launcher);
        await LoadAsync(fake, view);
        Task running = fake.RunAsync(view, "start");
        if (afterListening)
        {
            Task listening = WhenListening(view);
            launcher.Child.Listening.SetResult(new VmEndpoint(view.Address, 8766));
            await listening;
        }
        view.Dispose();
        await running;
        Assert.True(launcher.Child.Stopped);
        Assert.Equal(Loc.Current["Vm.Stopped"], view.ServiceStatus);
        await fake.RunAsync(view, "start");
        Assert.Single(launcher.Starts);
    }

    [Theory]
    [InlineData(false, "[100]", "Vm.NoKey")]
    [InlineData(true, "[]", "Vm.NoEplan")]
    [InlineData(true, "[100,200]", "Vm.PickInstance")]
    public async Task MissingPrerequisite_DisablesStartWithReason(bool key, string processes, string reason)
    {
        var fake = new FakeVm { StoredKey = key ? FakeVm.Key : null, Instances = "{\"schemaVersion\":1,\"processes\":" + processes + "}" };
        var launcher = new FakeVmLauncher();
        using VmView view = fake.Create(launcher: launcher);
        await LoadAsync(fake, view);
        Assert.Equal(Loc.Current[reason], view.DisabledReason("start"));
        await fake.RunAsync(view, "start");
        Assert.Empty(launcher.Starts);
    }

    [Fact]
    public async Task MultipleInstances_RequiresAnExplicitCurrentChoice_ThenPassesOnlyChosenPid()
    {
        var fake = new FakeVm { StoredKey = FakeVm.Key, Instances = "{\"schemaVersion\":1,\"processes\":[100,200]}" };
        var launcher = new FakeVmLauncher();
        using VmView view = fake.Create(launcher: launcher);
        await LoadAsync(fake, view);
        Assert.Null(view.SelectedProcess);
        view.SelectedProcess = 300;
        Assert.Equal(Loc.Current["Vm.PickInstance"], view.DisabledReason("start"));
        view.SelectedProcess = 200;
        Task running = fake.RunAsync(view, "start");
        Assert.Equal(200, Assert.Single(launcher.Starts).Process);
        await fake.RunAsync(view, "stop");
        await running;
        await fake.RunAsync(view, "instances");
        Assert.Null(view.SelectedProcess);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"processes\":[100]}")]
    [InlineData("{\"schemaVersion\":1,\"processes\":[0]}")]
    [InlineData("{\"schemaVersion\":1,\"processes\":[100,100]}")]
    [InlineData("{\"schemaVersion\":1,\"processes\":null}")]
    public async Task UnreadableInstances_NeverEnableStart(string report)
    {
        var fake = new FakeVm { StoredKey = FakeVm.Key, Instances = report };
        using VmView view = fake.Create();
        await LoadAsync(fake, view);
        Assert.Equal(Loc.Current["Vm.CheckInstances"], view.DisabledReason("start"));
    }

    [Fact]
    public async Task LaunchException_IsPlainRedactedAndAllowsRetry()
    {
        var fake = new FakeVm { StoredKey = FakeVm.Key };
        var launcher = new FakeVmLauncher { Error = new InvalidOperationException("Access refused " + FakeVm.Key) };
        using VmView view = fake.Create(launcher: launcher);
        await LoadAsync(fake, view);
        await fake.RunAsync(view, "start");
        Assert.True(view.ServiceFailed);
        Assert.Contains("Access refused", view.ServiceStatus, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeVm.Key, view.ServiceStatus, StringComparison.Ordinal);
        Assert.Empty(view.DisabledReason("start"));
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
        Assert.True(start.RedirectStandardError);
        Assert.True(start.RedirectStandardOutput);
        Assert.Empty(start.Arguments);
        string[] expected = ["serve", "--http", "--address", "192.168.56.2", "--port", "8766", .. pid is null ? Array.Empty<string>() : ["--process", "200"]];
        Assert.Equal(expected, start.ArgumentList);
        Assert.False(start.Environment.ContainsKey("EPLANMCP_WORKBENCH_CHILD"));
    }

    [Theory]
    [InlineData("eplanmcp.exe", "192.168.56.2", "8766", null)]
    [InlineData("C:\\app\\other.exe", "192.168.56.2", "8766", null)]
    [InlineData("C:\\app\\eplanmcp.exe", "0.0.0.0", "8766", null)]
    [InlineData("C:\\app\\eplanmcp.exe", "192.168.56.2;whoami", "8766", null)]
    [InlineData("C:\\app\\eplanmcp.exe", "192.168.56.2", "65536", null)]
    [InlineData("C:\\app\\eplanmcp.exe", "192.168.56.2", "8766", 0)]
    public void InvalidProcessArguments_AreRejectedBeforeLaunch(string executable, string address, string port, int? pid) =>
        Assert.Throws<ArgumentException>(() => VmProcess.StartInfo(executable, address, port, pid));

    [Theory]
    [InlineData(100, false, true)]
    [InlineData(200, false, false)]
    [InlineData(100, true, false)]
    public void ListeningSignal_RequiresBothChildPidAndUtcStartTime(int pid, bool reusedPid, bool matches)
    {
        const string json = "{\"processId\":100,\"processStart\":\"2026-10-22T12:00:00+00:00\",\"address\":\"192.168.56.2\",\"port\":8766}";
        var started = new DateTime(2026, 10, 22, 12, 0, reusedPid ? 1 : 0, DateTimeKind.Utc);
        VmEndpoint? bound = VmProcess.ReadState(json, pid, started);
        Assert.Equal(matches ? new VmEndpoint("192.168.56.2", 8766) : null, bound);
    }

    [Fact]
    public async Task ListeningTimeout_StopsChildAndExplainsRetry()
    {
        var fake = new FakeVm { StoredKey = FakeVm.Key };
        var launcher = new FakeVmLauncher();
        using VmView view = fake.Create(launcher: launcher);
        await LoadAsync(fake, view);
        Task running = fake.RunAsync(view, "start");
        launcher.Child.Listening.SetCanceled(TestContext.Current.CancellationToken);
        await running;
        Assert.True(launcher.Child.Stopped);
        Assert.True(view.ServiceFailed);
        Assert.Contains(Loc.Current["Vm.StartTimeout"], view.ServiceStatus, StringComparison.Ordinal);
    }

    private static async Task LoadAsync(FakeVm fake, VmView view)
    {
        await fake.RunAsync(view, "load");
        await fake.RunAsync(view, "instances");
    }

    private static Task WhenListening(VmView view)
    {
        var seen = new TaskCompletionSource();
        view.PropertyChanged += (_, _) => { if (view.Running && !view.Starting) { seen.TrySetResult(); } };
        return seen.Task.WaitAsync(TestContext.Current.CancellationToken);
    }
}
