using EplanMcp.Workbench.Commands;
using EplanMcp.Workbench.Vm;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Tests.Vm;

[Trait("Size", "Small")]
[Collection(WpfTestGroup.Name)]
public sealed class VmViewTests
{
    [Fact]
    public void Addresses_RejectLoopbackAndUnusableIPv4_AndPreferNonLinkLocal()
    {
        Assert.Equal(["10.0.0.2"], VmNetwork.Select(["::1", "127.0.0.1", "0.0.0.0", "169.254.1.1", "10.0.0.2", "10.0.0.2", "224.0.0.1"]));
        Assert.Equal(["169.254.1.1"], VmNetwork.Select(["127.0.0.1", "169.254.1.1"]));
        Assert.Empty(VmNetwork.Select(["127.1", "::1"]));
    }

    [Theory]
    [InlineData("192.168.56.1", true)]
    [InlineData("169.254.1.1", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("127.1", false)]
    [InlineData("0xC0A83801", false)]
    [InlineData("192.168.056.1", false)]
    [InlineData("192.168.56.1 ", false)]
    [InlineData(" 192.168.56.1", false)]
    [InlineData("192.168.56.1/32", false)]
    [InlineData("192.168.56.1,10.0.0.1", false)]
    [InlineData("192.168.56.1;whoami", false)]
    [InlineData("192.168.56.256", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("::ffff:192.168.56.1", false)]
    [InlineData("", false)]
    public void AddressValidation_IsStrict(string address, bool expected) => Assert.Equal(expected, VmNetwork.IsAddress(address));

    [Theory]
    [InlineData("1", true)]
    [InlineData("65535", true)]
    [InlineData("8766", true)]
    [InlineData("0", false)]
    [InlineData("65536", false)]
    [InlineData("-1", false)]
    [InlineData("+8766", false)]
    [InlineData(" 8766", false)]
    [InlineData("8766 ", false)]
    [InlineData("8.766", false)]
    [InlineData("8766;whoami", false)]
    [InlineData("9999999999999", false)]
    public void PortValidation_IsStrict(string port, bool expected) => Assert.Equal(expected, VmNetwork.IsPort(port));

    [Fact]
    public async Task Key_CreateReadMaskShowCopyDelete_OnlyAfterConfirmation()
    {
        var fake = new FakeVm();
        VmView view = fake.Create();
        Assert.Equal(("192.168.56.2", "8766"), (view.Address, view.Port));
        await fake.RunAsync(view, "load");
        Assert.False(view.HasKey);
        await fake.RunAsync(view, "create");
        Assert.Empty(fake.Questions);
        Assert.Equal(["show", "create", "show"], fake.Host.Runs.Select(r => r.Arguments[1]));
        Assert.Equal("••••••••", view.DisplayKey);
        Assert.Empty(fake.Clipboard);
        await fake.RunAsync(view, "show");
        Assert.Equal(FakeVm.Key, view.DisplayKey);
        await fake.RunAsync(view, "show");
        Assert.Equal("••••••••", view.DisplayKey);
        await fake.RunAsync(view, "copy");
        Assert.Equal([FakeVm.Key], fake.Clipboard);
        fake.Confirmed = false;
        await fake.RunAsync(view, "create");
        await fake.RunAsync(view, "delete");
        Assert.Equal(["Regenerate", "Delete"], fake.Questions);
        Assert.Equal(3, fake.Host.Runs.Count);
        fake.Confirmed = true;
        await fake.RunAsync(view, "create");
        Assert.Equal("••••••••", view.DisplayKey);
        await fake.RunAsync(view, "delete");
        Assert.False(view.HasKey);
        Assert.Empty(view.DisplayKey);
        Assert.NotEmpty(view.DisabledReason("copy"));
        Assert.All(fake.Host.Runs, r => Assert.DoesNotContain(FakeVm.Key, string.Join(' ', r.Arguments), StringComparison.Ordinal));
        Assert.DoesNotContain(FakeVm.Key, view.Message, StringComparison.Ordinal);
        int questions = fake.Questions.Count;
        await fake.RunAsync(view, "create");
        Assert.Equal(questions, fake.Questions.Count); // A successful deletion established that no old key exists.
    }

    [Fact]
    public async Task Key_UnknownPresence_ConfirmsBeforeOverwriting()
    {
        var fake = new FakeVm { Confirmed = false };
        VmView view = fake.Create();
        await fake.RunAsync(view, "create");
        Assert.Equal(["Regenerate"], fake.Questions);
        Assert.Empty(fake.Host.Runs);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("delete")]
    [InlineData("load")]
    public async Task Key_FailureOrException_RedactsAllOutputAndLeavesNoRevealedKey(string action)
    {
        var fake = new FakeVm { StoredKey = FakeVm.Key };
        VmView view = fake.Create();
        foreach (bool exception in new[] { false, true })
        {
            fake.Host.Answer = _ => new HostRun(0, FakeVm.Key);
            await fake.RunAsync(view, "load");
            await fake.RunAsync(view, "show");
            fake.Host.Answer = _ => exception ? throw new HostCommandException(HostFailure.Failed, FakeVm.Key) { Errors = FakeVm.Key }
                : new HostRun(1, FakeVm.Key, FakeVm.Key);
            await fake.RunAsync(view, action);
            Assert.False(view.Busy);
            Assert.DoesNotContain(FakeVm.Key, view.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(FakeVm.Key, view.DisplayKey, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ClipboardFailure_IsRedacted_AndDoesNotWriteSettingsOrRerunHost()
    {
        var fake = new FakeVm { StoredKey = FakeVm.Key };
        VmView view = fake.Create();
        await fake.RunAsync(view, "load");
        await view.RunAsync("copy", _ => true, _ => throw new InvalidOperationException(FakeVm.Key), CancellationToken.None);
        Assert.DoesNotContain(FakeVm.Key, view.Message, StringComparison.Ordinal);
        Assert.Single(fake.Host.Runs);
        Assert.Equal("••••••••", view.DisplayKey);
    }

    [Theory]
    [InlineData("allow", 0)]
    [InlineData("remove", 0)]
    [InlineData("allow", 1)]
    [InlineData("remove", 1)]
    [InlineData("allow", null)]
    [InlineData("remove", null)]
    public async Task Firewall_ElevatesOnlyMutation_ThenReadsActualStateUnelevated(string action, int? exit)
    {
        var fake = new FakeVm { ExitCode = exit };
        VmView view = fake.Create();
        await fake.RunAsync(view, action);
        Assert.Equal((fake.Host.Executable, action == "remove", "8766", "192.168.56.1"), Assert.Single(fake.Elevated));
        Assert.Equal(["vm-firewall", "show", "--port", "8766", "--host-address", "192.168.56.1", "--format", "json"], Assert.Single(fake.Host.Runs).Arguments);
        Assert.Equal(Loc.Current[exit is null ? "Vm.Cancelled" : exit == 0 ? "Vm.Done" : "Vm.Failed"], view.Message);
        Assert.Equal(Loc.Current["Vm.Absent"], view.Firewall);
        Assert.False(view.Busy);
    }

    [Theory]
    [InlineData("bad json")]
    [InlineData("{\"schemaVersion\":2,\"rules\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"rules\":[null]}")]
    [InlineData("{\"schemaVersion\":1,\"rules\":[],\"secret\":\"ignored\"}")]
    public async Task Firewall_UnreadableOrUnsupportedResult_IsAnErrorWithNoPartialState(string json)
    {
        var fake = new FakeVm { Rules = json };
        VmView view = fake.Create();
        await fake.RunAsync(view, "refresh");
        Assert.Equal(Loc.Current["Vm.Failed"], view.Message);
        Assert.Equal(Loc.Current["Vm.Unknown"], view.Firewall);
        Assert.Empty(fake.Elevated);
    }

    [Fact]
    public async Task Firewall_CancelledConsent_StillHasOwnMessageWhenRefreshFails()
    {
        var fake = new FakeVm { ExitCode = null, Rules = "bad" };
        VmView view = fake.Create();
        await fake.RunAsync(view, "allow");
        Assert.Equal(Loc.Current["Vm.CancelledUnknown"], view.Message);
    }

    [Fact]
    public async Task Firewall_LaunchFailure_IsRedactedAndReenablesCommands()
    {
        var fake = new FakeVm { Error = new InvalidOperationException(FakeVm.Key) };
        VmView view = fake.Create();
        await fake.RunAsync(view, "allow");
        Assert.Equal(Loc.Current["Vm.Failed"], view.Message);
        Assert.False(view.Busy);
        Assert.Empty(view.DisabledReason("allow"));
    }

    [Fact]
    public async Task ValidationOrMissingHost_PreventsCommands_AndExplainsWhy()
    {
        var fake = new FakeVm();
        VmView view = fake.Create(available: false);
        await fake.RunAsync(view, "create");
        Assert.NotEmpty(view.Reason);
        view = fake.Create();
        view.Address = "10.0.0.3";
        Assert.NotEmpty(view.AddressError);
        view.Port = "65536";
        await fake.RunAsync(view, "allow");
        view.Port = "8766";
        view.HostAddress = "192.168.56.1/24";
        await fake.RunAsync(view, "remove");
        await fake.RunAsync(view, "refresh");
        Assert.NotEmpty(view.DisabledReason("allow"));
        Assert.Empty(fake.Host.Runs);
        Assert.Empty(fake.Elevated);
    }

    [Fact]
    public async Task RunningCommand_DisablesReentryUntilCompletion()
    {
        var fake = new FakeVm();
        VmView view = fake.Create();
        TaskCompletionSource<HostRun> held = fake.Host.Hold();
        Task pending = fake.RunAsync(view, "load");
        Assert.True(view.Busy);
        await fake.RunAsync(view, "create");
        Assert.Single(fake.Host.Runs);
        held.SetResult(new HostRun(1, "no key"));
        await pending;
        Assert.False(view.Busy);
    }
}
