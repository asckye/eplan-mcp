using System.Text.Json.Nodes;
using EplanMcp.Workbench.Commands;
using EplanMcp.Workbench.Status;
using EplanMcp.Workbench.Tests.Commands;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Tests.Status;

/// <summary>Every process is a fake. Running these tests never discovers, starts or unregisters EPLAN.</summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Small")]
public sealed class AddinViewTests(WpfContext wpf)
{
    internal const string Doctor = """{"schemaVersion":1,"hostVersion":"0.1.0","status":"ok","checks":[]}""";
    internal const string Variants = """{"schemaVersion":1,"variants":["Electric P8","Pro Panel"]}""";
    private readonly FakeHost _host = new() { Answer = args => new(0, args[0] == "addin-variants" ? Variants : Doctor) };
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("success", 1, "已注册", "Registered")]
    [InlineData("install", 3, "已拒绝", "Refused")]
    [InlineData("running", 1, "请先关闭所有 EPLAN", "Close every EPLAN")]
    [InlineData("notConfirmed", 1, "未确认", "Not confirmed")]
    [InlineData("uninstall", 1, "已撤销", "Unregistered")]
    public async Task RegisterAndUnregister_ShowEveryResultAndTheHostsRecoveryText(string example, int count, string zh, string en)
    {
        var view = new StatusView(new HostCommands(_host), () => true);
        await view.RunAsync(Token);
        Assert.Equal(["Electric P8", "Pro Panel"], view.Variants.Select(v => v.Name));
        view.Variants[1].IsSelected = true;
        _host.Answer = _ => Result(example);

        await view.RegisterAsync(example == "uninstall", Token);

        Assert.Equal(StatusState.Ready, view.State);
        Assert.Equal(example == "uninstall" ? ["uninstall-addin", "--format", "json"] : ["install-addin", "--variant", "Pro Panel", "--format", "json"], _host.Runs[^1].Arguments);
        wpf.RunWithLanguage(AppLanguage.Chinese, () =>
        {
            StatusGroup group = view.Groups.Single(g => g.Key == "Addin.Results");
            Assert.Equal(count, group.Lines.Count);
            Assert.Contains(zh, string.Join(" ", group.Lines.Select(l => l.Text)), StringComparison.Ordinal);
            if (example == "notConfirmed")
            {
                Assert.Equal(CheckStatus.Warning, group.Lines[0].Status);
                Assert.Contains("登录、授权", group.Lines[0].Text, StringComparison.Ordinal);
            }

            Loc.Current.Language = AppLanguage.English;
            Assert.Contains(en, string.Join(" ", group.Lines.Select(l => l.Text)), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task NoSelectionDoesNotAccidentallyRegisterAll_AndAnOngoingCommandCannotBeStartedTwice()
    {
        var view = new StatusView(new HostCommands(_host), () => true);
        await view.RunAsync(Token);
        await view.RegisterAsync(false, Token);
        Assert.Equal(2, _host.Runs.Count);
        view.Variants[0].IsSelected = true;
        TaskCompletionSource<HostRun> held = _host.Hold();

        Task pending = view.RegisterAsync(false, Token);

        Assert.False(pending.IsCompleted);
        Assert.Equal(StatusState.Running, view.State);
        await view.RegisterAsync(true, Token);
        Assert.Equal(3, _host.Runs.Count);
        held.SetResult(Result("success"));
        await pending;
        Assert.Equal(StatusState.Ready, view.State);
    }

    [Fact]
    public async Task AnUnreadableResultOrTimeout_LeavesThePageUsableAndShowsNoPartialResults()
    {
        var view = new StatusView(new HostCommands(_host), () => true);
        await view.RunAsync(Token);
        view.Variants[0].IsSelected = true;
        _host.Answer = _ => Result("success") with { Output = Result("success").Output.Replace("registered", "unknown_result", StringComparison.Ordinal) };
        await view.RegisterAsync(false, Token);
        Assert.Equal(StatusState.Failed, view.State);
        Assert.NotNull(view.Error);
        Assert.DoesNotContain(view.Groups, g => g.Key == "Addin.Results");
        _host.Answer = _ => throw new HostCommandException(HostFailure.TimedOut, "Timeout");
        await view.RegisterAsync(true, Token);
        Assert.Equal(StatusState.Failed, view.State);
        Assert.NotNull(view.ErrorDetails);
    }

    internal static HostRun Result(string example)
    {
        JsonNode report = JsonNode.Parse(Snapshots.Example("addin-json", example is "success" or "running" ? "install" : example))!;
        if (example is "success" or "running")
        {
            JsonNode row = report["results"]![example == "success" ? 1 : 2]!.DeepClone();
            report["results"] = new JsonArray(row);
        }

        return new(example == "success" || example == "uninstall" ? 0 : 1, report.ToJsonString());
    }
}
