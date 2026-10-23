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
    internal static readonly string Key = new('A', 43);
    private readonly FakeHost _host = new() { Answer = Answer };
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ClientsView View(bool available = true)
    {
        var view = new ClientsView(new(available ? _host : null), "C:\\unused", _ => false);
        view.SetVmMode(true);
        view.SetVmInput(Address, "8766", Key);
        return view;
    }

    [Fact]
    public async Task ModeSwitch_ClearsSecretsSelectionsResultsAndLocalPluginState()
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
        view.SetVmMode(true);
        Assert.NotEmpty(view.VmKeyError);
        await view.TestConnectionAsync(Token);
        Assert.Equal(3, _host.Runs.Count);
    }

    [Theory]
    [InlineData("localhost", "8766", "valid", "address")]
    [InlineData("192.168.056.2", "8766", "valid", "address")]
    [InlineData("192.168.56.2", "0", "valid", "port")]
    [InlineData("192.168.56.2", "65536", "valid", "port")]
    [InlineData("192.168.56.2", "8766 ", "valid", "port")]
    [InlineData("192.168.56.2", "8766", "", "key")]
    [InlineData("192.168.56.2", "8766", "short", "key")]
    [InlineData("192.168.56.2", "8766", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA ", "key")]
    [InlineData("192.168.56.2", "8766", "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", "key")]
    public async Task InvalidInput_DisablesTestPreviewWriteAndDetection_WithTheFieldReason(string address, string port, string key, string error)
    {
        ClientsView view = View();
        view.SetVmInput(address, port, key == "valid" ? Key : key);
        view.Cards[0].IsSelected = true;
        Assert.Equal(error switch { "address" => view.VmAddressError, "port" => view.VmPortError, _ => view.VmKeyError }, view.VmReason());
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
    public async Task PreviewWriteRemove_UseOnlySelectedSupportedClients_WithStdinNeverArguments()
    {
        ClientsView view = View();
        foreach (ClientCard card in view.Cards.Where(c => c.Name is "cursor" or "codex"))
        { card.IsSelected = true; }
        await view.PreviewAsync(Token);
        Assert.Contains("+ ", view.Diff(), StringComparison.Ordinal);
        Assert.Contains("<key>", view.Diff(), StringComparison.Ordinal);
        Assert.DoesNotContain(Key, view.Diff(), StringComparison.Ordinal);
        await view.ApplyAsync(ClientConfigMode.Write, Token);
        Assert.All(view.Selected, c => Assert.Equal(ClientResultKind.Written, c.Result!.Result));
        Assert.All(view.Selected, c => Assert.DoesNotContain(Key, c.ResultText!, StringComparison.Ordinal));
        await view.ApplyAsync(ClientConfigMode.Remove, Token);
        Assert.All(view.Selected, c => Assert.Equal(ClientResultKind.Removed, c.Result!.Result));
        Assert.Equal([Key, Key, null], _host.Inputs);
        Assert.Equal(
            [["client-config", "codex", "cursor", "--vm", "--url", Url, "--key-stdin", "--dry-run", "--format", "json"],
             ["client-config", "codex", "cursor", "--vm", "--url", Url, "--key-stdin", "--format", "json"],
             ["client-config", "codex", "cursor", "--vm", "--remove", "--format", "json"]], _host.Runs.Select(r => r.Arguments));
        Assert.All(_host.Runs, r => Assert.DoesNotContain(Key, string.Join(' ', r.Arguments), StringComparison.Ordinal));
        Assert.Equal([HostCommands.ClientConfigTimeout, HostCommands.ClientConfigWriteTimeout, HostCommands.ClientConfigWriteTimeout], _host.Runs.Select(r => r.Timeout));
    }

    [Theory]
    [InlineData("success")]
    [InlineData("unreachable")]
    [InlineData("timed_out")]
    [InlineData("unauthorized")]
    [InlineData("forbidden")]
    [InlineData("rate_limited")]
    [InlineData("protocol_error")]
    public async Task TestConnection_ReadsEveryHostSnapshot_AndGivesADistinctResult(string example)
    {
        _host.Answer = _ => new(example == "success" ? 0 : 1, Snapshots.Example("vm-test-json", example), Key);
        ClientsView view = View();
        await view.TestConnectionAsync(Token);
        Assert.Equal(example == "success" ? ClientsState.Ready : ClientsState.Failed, view.State);
        Assert.Equal(["vm-test", "--url", Url, "--key-stdin", "--format", "json"], Assert.Single(_host.Runs).Arguments);
        Assert.Equal(Key, Assert.Single(_host.Inputs));
        Assert.Equal(TimeSpan.FromSeconds(30), _host.Runs[0].Timeout);
        if (example == "success")
        {
            Assert.Contains("eplan-vm", view.VmStatus, StringComparison.Ordinal);
            Assert.Contains("1.2.3", view.VmStatus, StringComparison.Ordinal);
            Assert.Contains("42", view.VmStatus, StringComparison.Ordinal);
            Assert.Null(view.Error);
        }
        else
        { Assert.Equal(Loc.Current["VmHost." + ClientsView.Key(example.Replace('_', '-'))], view.Error); }
        Assert.DoesNotContain(Key, view.VmStatus + view.Error + view.ErrorDetails, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task UnknownOrWrongSchemaVersions_AreRefusedOnTheVmPath(int version)
    {
        JsonNode report = JsonNode.Parse(Snapshots.Example("client-config-json", "vmPreview"))!;
        report["schemaVersion"] = version;
        _host.Answer = _ => new(0, report.ToJsonString());
        ClientsView view = View();
        view.Cards[0].IsSelected = true;
        await view.PreviewAsync(Token);
        Assert.Equal(version == 2 ? ClientsState.Ready : ClientsState.Failed, view.State);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("unknown_field")]
    [InlineData("missing_field")]
    [InlineData("unknown_failure")]
    [InlineData("partial_success")]
    [InlineData("null_server")]
    [InlineData("negative_count")]
    [InlineData("duplicate_field")]
    public async Task MalformedConnectionReports_AreRefusedAsAWhole(string problem)
    {
        JsonNode report = JsonNode.Parse(Snapshots.Example("vm-test-json", "success"))!;
        switch (problem)
        {
            case "version":
                report["schemaVersion"] = 2;
                break;
            case "unknown_field":
                report[Key] = true;
                break;
            case "missing_field":
                report.AsObject().Remove("failure");
                break;
            case "unknown_failure":
                report["failure"] = Key;
                break;
            case "partial_success":
                report["failure"] = "unauthorized";
                break;
            case "null_server":
                report["server"] = null;
                break;
            case "duplicate_field":
                break;
            default:
                report["toolCount"] = -1;
                break;
        }
        string output = report.ToJsonString();
        if (problem == "duplicate_field")
        { output = output.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal); }
        _host.Answer = _ => new(0, output, Key);
        ClientsView view = View();
        await view.TestConnectionAsync(Token);
        Assert.Equal(ClientsState.Failed, view.State);
        Assert.NotNull(view.Error);
        Assert.DoesNotContain(Key, view.Error + view.ErrorDetails + view.VmStatus, StringComparison.Ordinal);
        Assert.DoesNotContain("eplan-vm", view.VmStatus, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("exception")]
    [InlineData("malformed")]
    [InlineData("exit_code")]
    public async Task SecretDiagnostics_AreWithheldIncludingInnerExceptions(string problem)
    {
        _host.Answer = _ => problem switch
        {
            "timeout" => throw new HostCommandException(HostFailure.TimedOut, Key, new IOException(Key)) { Errors = Key },
            "exception" => throw new InvalidOperationException(Key),
            "malformed" => new(0, Key, Key),
            _ => new(2, Key, Key),
        };
        ClientsView view = View();
        await view.TestConnectionAsync(Token);
        Assert.Equal(ClientsState.Failed, view.State);
        Assert.DoesNotContain(Key, view.Error + view.ErrorDetails + view.VmStatus, StringComparison.Ordinal);
        var error = await Assert.ThrowsAsync<HostCommandException>(() => new HostCommands(_host).TestVmAsync(Url, Key, Token));
        Assert.DoesNotContain(Key, error.ToString() + error.Errors + error.CommandLine, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task EncodedAndOldKeysInSuccessReports_AreRedactedBeforeDisplay()
    {
        string oldKey = new string('B', 42) + "A";
        string encoded = string.Concat(Enumerable.Repeat("\\u0041", 43));
        _host.Answer = _ => new(0, Snapshots.Example("vm-test-json", "success").Replace("eplan-vm", encoded, StringComparison.Ordinal).Replace("1.2.3", oldKey, StringComparison.Ordinal));
        ClientsView view = View();
        await view.TestConnectionAsync(Token);
        Assert.Equal(ClientsState.Ready, view.State);
        Assert.DoesNotContain(Key, view.VmStatus, StringComparison.Ordinal);
        Assert.DoesNotContain(oldKey, view.VmStatus, StringComparison.Ordinal);
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
        view.SetVmInput("10.0.0.1", "1", string.Empty);
        await view.PreviewAsync(Token);
        await view.ApplyAsync(ClientConfigMode.Remove, Token);
        Assert.Single(_host.Runs);
        Assert.True(view.VmMode);
        held.SetResult(new(0, Snapshots.Example("vm-test-json", "success")));
        await running;
        Assert.Empty(view.VmReason());
        await view.TestConnectionAsync(Token);
        Assert.Equal([Key, Key], _host.Inputs);
        Assert.All(_host.Runs, r => Assert.Contains(Url, r.Arguments));
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
        { return new(0, Snapshots.Example("vm-test-json", "success")); }
        bool vm = args.Contains("--vm"), remove = args.Contains("--remove"), preview = args.Contains("--dry-run");
        JsonNode report = JsonNode.Parse(Snapshots.Example("client-config-json", vm ? "vmPreview" : "dryRun"))!;
        report["mode"] = remove ? "remove" : preview ? "dry_run" : "write";
        report["clients"] = new JsonArray([.. args.Skip(1).TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)).Select(name => (JsonNode)new JsonObject
        {
            ["client"] = name, ["file"] = "C:\\unused\\" + name, ["action"] = remove ? "remove" : "write",
            ["result"] = remove ? "removed" : preview ? "would_write" : "written",
            ["before"] = "Bearer " + Key, ["after"] = remove ? null : "Bearer " + Key,
            ["backup"] = preview ? null : "C:\\unused\\" + Key, ["notes"] = new JsonArray(), ["problem"] = null,
        })]);
        return new(0, report.ToJsonString());
    }
}
