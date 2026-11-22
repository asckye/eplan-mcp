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
        using VmView view = fake.Create();
        await fake.RunAsync(view, action);
        Assert.Equal((fake.Host.Executable, action == "remove", "8766"), Assert.Single(fake.Elevated));
        Assert.Equal(["vm-firewall", "show", "--port", "8766", "--format", "json"], Assert.Single(fake.Host.Runs).Arguments);
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
        using VmView view = fake.Create();
        await fake.RunAsync(view, "refresh");
        Assert.Equal(Loc.Current["Vm.Failed"], view.Message);
        Assert.Equal(Loc.Current["Vm.Unknown"], view.Firewall);
        Assert.Empty(fake.Elevated);
    }

    [Fact]
    public async Task Firewall_CancelledConsent_StillHasOwnMessageWhenRefreshFails()
    {
        var fake = new FakeVm { ExitCode = null, Rules = "bad" };
        using VmView view = fake.Create();
        await fake.RunAsync(view, "allow");
        Assert.Equal(Loc.Current["Vm.CancelledUnknown"], view.Message);
    }

    [Fact]
    public async Task ValidationOrMissingHost_PreventsCommands_AndExplainsWhy()
    {
        var fake = new FakeVm();
        using VmView unavailable = fake.Create(available: false);
        await fake.RunAsync(unavailable, "refresh");
        Assert.NotEmpty(unavailable.Reason);
        using VmView view = fake.Create();
        view.Address = "10.0.0.3";
        Assert.NotEmpty(view.AddressError);
        view.Port = "65536";
        await fake.RunAsync(view, "allow");
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
        using VmView view = fake.Create();
        TaskCompletionSource<HostRun> held = fake.Host.Hold();
        Task pending = fake.RunAsync(view, "refresh");
        Assert.True(view.Busy);
        await fake.RunAsync(view, "allow");
        Assert.Single(fake.Host.Runs);
        Assert.Empty(fake.Elevated);
        held.SetResult(new HostRun(0, FakeVm.EmptyRules));
        await pending;
        Assert.False(view.Busy);
    }
}
