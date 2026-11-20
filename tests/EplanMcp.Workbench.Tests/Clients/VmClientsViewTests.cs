using System.Text.Json.Nodes;
using EplanMcp.Host.ClientConfig;
using EplanMcp.Workbench.Clients;
using EplanMcp.Workbench.Commands;
using EplanMcp.Workbench.Tests.Commands;
using TiaOpenness.Gui.Localization;
using ClientConfigMode = EplanMcp.Workbench.Commands.ClientConfigMode;

namespace EplanMcp.Workbench.Tests.Clients;

[Trait("Size", "Small")]
[Collection(WpfTestGroup.Name)]
public sealed class VmClientsViewTests
{
    internal const string Address = "192.168.56.2";
    internal const string Url = "http://192.168.56.2:8766/mcp";
    private readonly FakeHost _host = new() { Answer = Answer };
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ClientsView View(bool available = true)
    {
        var view = new ClientsView(new(available ? _host : null), "C:\\unused", _ => false);
        view.SetVmMode(true);
        view.SetVmInput(Address, "8766");
        return view;
    }

    [Fact]
    public async Task ModeSwitch_ClearsSelectionsResultsAndLocalPluginState()
    {
        ClientsView view = View();
        Assert.Equal("8766", view.VmPort);
        Assert.Equal(McpClient.All.Where(RemoteServer.Supports).Select(c => c.Name), view.Cards.Select(c => c.Name));
        Assert.DoesNotContain(view.Cards, c => c.Name == "claude-desktop");
        view.Cards.Single(c => c.Name == "claude-code").IsSelected = true;
        await view.PreviewAsync(Token);
        Assert.True(view.Cards[0].CanWrite);
        await view.ApplyAsync(ClientConfigMode.Write, Token);
        Assert.NotNull(view.Cards[0].Result);
        await view.GeneratePluginAsync(true, Token);
        Assert.Equal(2, _host.Runs.Count);

        view.SetVmMode(false);
        Assert.Equal(11, view.Cards.Count);
        Assert.Equal(ClientsState.Idle, view.State);
        Assert.All(view.Cards, c => { Assert.False(c.IsSelected); Assert.Null(c.Preview); Assert.Null(c.Result); });
        Assert.All(view.PluginCommands, Assert.Empty);
        Assert.Null(view.PluginPreview);
        Assert.Null(view.Error);
        await view.DetectAsync(Token);
        Assert.Equal(["client-config", .. ClientsView.Names, "--dry-run", "--format", "json"], _host.Runs[^1].Arguments);
        Assert.Null(_host.Inputs[^1]);
    }

    [Theory]
    [InlineData("localhost", "8766", "address")]
    [InlineData("192.168.056.2", "8766", "address")]
    [InlineData("192.168.56.2", "0", "port")]
    [InlineData("192.168.56.2", "65536", "port")]
    [InlineData("192.168.56.2", "8766 ", "port")]
    public async Task InvalidInput_DisablesTestPreviewWriteAndDetection_WithTheFieldReason(string address, string port, string error)
    {
        ClientsView view = View();
        view.SetVmInput(address, port);
        view.Cards[0].IsSelected = true;
        Assert.Equal(error == "address" ? view.VmAddressError : view.VmPortError, view.VmReason());
        Assert.NotEmpty(view.VmReason());
        await view.TestConnectionAsync(Token);
        await view.PreviewAsync(Token);
        await view.ApplyAsync(ClientConfigMode.Write, Token);
        await view.DetectAsync(Token);
        Assert.Empty(_host.Runs);
        Assert.Empty(view.VmReason(remove: true));
        await view.ApplyAsync(ClientConfigMode.Remove, Token);
        Assert.Contains("--remove", Assert.Single(_host.Runs).Arguments);
        Assert.Null(Assert.Single(_host.Inputs));
    }

    [Fact]
    public async Task PreviewWriteRemove_UseKeyFreeVmArguments()
    {
        ClientsView view = View();
        foreach (ClientCard card in view.Cards.Where(c => c.Name is "cursor" or "codex"))
        {
            card.IsSelected = true;
        }

        await view.PreviewAsync(Token);
        Assert.Contains("+ ", view.Diff(), StringComparison.Ordinal);
        await view.ApplyAsync(ClientConfigMode.Write, Token);
        Assert.All(view.Selected, c => Assert.Equal(ClientResultKind.Written, c.Result!.Result));
        await view.ApplyAsync(ClientConfigMode.Remove, Token);
        Assert.All(view.Selected, c => Assert.Equal(ClientResultKind.Removed, c.Result!.Result));
        Assert.Equal([null, null, null], _host.Inputs);
        Assert.Equal(
            [["client-config", "codex", "cursor", "--vm", "--url", Url, "--dry-run", "--format", "json"],
             ["client-config", "codex", "cursor", "--vm", "--url", Url, "--format", "json"],
             ["client-config", "codex", "cursor", "--vm", "--remove", "--format", "json"]], _host.Runs.Select(r => r.Arguments));
        Assert.Equal([HostCommands.ClientConfigTimeout, HostCommands.ClientConfigWriteTimeout, HostCommands.ClientConfigWriteTimeout], _host.Runs.Select(r => r.Timeout));
    }

    [Theory]
    [InlineData("success")]
    [InlineData("unreachable")]
    [InlineData("timed_out")]
    [InlineData("forbidden")]
    [InlineData("protocol_error")]
    public async Task TestConnection_ReadsEveryHostSnapshot_WithoutKey(string example)
    {
        _host.Answer = _ => new(example == "success" ? 0 : 1, Snapshots.Example("vm-test-json", example));
        ClientsView view = View();
        await view.TestConnectionAsync(Token);
        Assert.Equal(example == "success" ? ClientsState.Ready : ClientsState.Failed, view.State);
        Assert.Equal(["vm-test", "--url", Url, "--format", "json"], Assert.Single(_host.Runs).Arguments);
        Assert.Null(Assert.Single(_host.Inputs));
        if (example == "success")
        {
            Assert.Contains("eplan-vm", view.VmStatus, StringComparison.Ordinal);
            Assert.Contains("1.2.3", view.VmStatus, StringComparison.Ordinal);
            Assert.Contains("42", view.VmStatus, StringComparison.Ordinal);
            Assert.Null(view.Error);
        }
        else
        {
            Assert.Equal(Loc.Current["VmHost." + ClientsView.Key(example.Replace('_', '-'))], view.Error);
        }
    }

    [Fact]
    public async Task WhileRunning_ModeInputsAndOtherCommandsAreLocked_ThenRecover()
    {
        ClientsView view = View();
        view.Cards[0].IsSelected = true;
        TaskCompletionSource<HostRun> held = _host.Hold();
        Task running = view.TestConnectionAsync(Token);
        Assert.Equal(ClientsState.Running, view.State);
        Assert.NotEmpty(view.VmReason(remove: true));
        view.SetVmMode(false);
        view.SetVmInput("10.0.0.1", "1");
        await view.PreviewAsync(Token);
        await view.ApplyAsync(ClientConfigMode.Remove, Token);
        Assert.Single(_host.Runs);
        Assert.True(view.VmMode);
        held.SetResult(new(0, Snapshots.Example("vm-test-json", "success")));
        await running;
        Assert.Empty(view.VmReason());
    }

    [Fact]
    public async Task MissingHost_DisablesAllActionsWithAReason()
    {
        ClientsView view = View(available: false);
        Assert.Equal(Loc.Current["Host.Missing"], view.VmReason());
        Assert.Equal(view.VmReason(), view.VmReason(remove: true));
        await view.TestConnectionAsync(Token);
        await view.DetectAsync(Token);
        Assert.Empty(_host.Runs);
    }

    internal static HostRun Answer(string[] args)
    {
        if (args[0] == "vm-test")
        {
            return new(0, Snapshots.Example("vm-test-json", "success"));
        }

        bool vm = args.Contains("--vm"), remove = args.Contains("--remove"), preview = args.Contains("--dry-run");
        JsonNode report = JsonNode.Parse(Snapshots.Example("client-config-json", vm ? "vmPreview" : "dryRun"))!;
        report["mode"] = remove ? "remove" : preview ? "dry_run" : "write";
        report["clients"] = new JsonArray([.. args.Skip(1).TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)).Select(name => (JsonNode)new JsonObject
        {
            ["client"] = name, ["file"] = "C:\\unused\\" + name, ["action"] = remove ? "remove" : "write",
            ["result"] = remove ? "removed" : preview ? "would_write" : "written",
            ["before"] = null, ["after"] = remove ? null : Url,
            ["backup"] = preview ? null : "C:\\unused\\backup", ["notes"] = new JsonArray(), ["problem"] = null,
        })]);
        return new(0, report.ToJsonString());
    }
}
