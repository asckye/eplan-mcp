using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class ActionTryContractTests
{
    private static readonly ActionCatalogData _catalog = new([
        ActionCatalogReader.Create("XViewAction", "ui", "DETAIL", null, null,
            [JsonSerializer.SerializeToElement(new { name = "Factor" }), JsonSerializer.SerializeToElement(new { name = "Enabled" }), JsonSerializer.SerializeToElement(new { name = "Label" })], null, "fixture"),
        ActionCatalogReader.Create("XViewAction", "ui", "DETAIL", null, null, [JsonSerializer.SerializeToElement(new { name = "Other" })], null, "variant"),
        ActionCatalogReader.Create("XHiddenAction", "official", null, null, null, [], "unsafe", "fixture")], "2026.0.3", []);

    [Fact]
    public void SimpleValues_ArePreservedWithoutCommandLineParsing()
    {
        var args = Check("{\"Factor\":1.25,\"Enabled\":true,\"Label\":\"two words 中文\"}");
        Assert.Equal(["1.25", "true", "two words 中文"], args.Parameters.Select(p => p.Value));
        Assert.Equal("DETAIL", args.Type);
    }

    [Fact]
    public void ParameterCountAndLength_AreBounded()
    {
        Assert.Throws<BridgeCallException>(() => Check(JsonSerializer.Serialize(new { Label = new string('x', 101) })));
        var values = Enumerable.Range(0, 33).ToDictionary(i => "p" + i.ToString(CultureInfo.InvariantCulture), _ => 1);
        Assert.Throws<BridgeCallException>(() => Check(JsonSerializer.Serialize(values)));
    }

    [Theory]
    [InlineData("{\"Unknown\":1}")]
    [InlineData("{\"Factor\":1,\"Other\":2}")]
    [InlineData("{\"Factor\":1,\"factor\":2}")]
    [InlineData("{\"Factor\":1,\"Factor\":2}")]
    [InlineData("{\"Factor\":null}")]
    [InlineData("{\"Factor\":[]}")]
    [InlineData("{\"Factor\":{}}")]
    [InlineData("{\"TYPE\":\"DETAIL\"}")]
    [InlineData("[]")]
    [InlineData("null")]
    public void InvalidShapesAndUnknownOrDuplicateNames_AreRejected(string json) =>
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeCallException>(() => Check(json)).Error.Reason);

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\"b")]
    [InlineData("a'b")]
    [InlineData("a\nb")]
    [InlineData("a\0b")]
    [InlineData("../escape")]
    [InlineData("D:\\real")]
    [InlineData("\\\\server\\share")]
    [InlineData("$(MD_PROJECTS)")]
    [InlineData("%USERPROFILE%")]
    public void InjectionAndPaths_AreRejected(string value) =>
        Assert.Throws<BridgeCallException>(() => Check(JsonSerializer.Serialize(new { Label = value })));

    [Theory]
    [InlineData("XEsExitAction")]
    [InlineData("RestartEplan")]
    [InlineData("ProjectOpen")]
    [InlineData("ProjectAction")]
    [InlineData("XPrjCloseAction")]
    [InlineData("ProjectCreate")]
    [InlineData("XSettingsImport")]
    [InlineData("WorkstationSetup")]
    [InlineData("UserDataChange")]
    [InlineData("LicenseBorrow")]
    [InlineData("UpdateService")]
    [InlineData("CloudUpload")]
    [InlineData("NetworkConnect")]
    [InlineData("Print")]
    [InlineData("RunProgram")]
    [InlineData("ExecuteScript")]
    [InlineData("EplApiModuleAction")]
    public void DenyData_CoversForbiddenCategoriesAndCaseVariants(string name)
    {
        Assert.True(ActionTryPolicy.Denied("action", name));
        Assert.True(ActionTryPolicy.Denied("action", name.ToUpperInvariant()));
        Assert.True(ActionTryPolicy.Denied("action", name.ToLowerInvariant()));
    }

    [Theory]
    [InlineData("PROJECTNAME")]
    [InlineData("Target")]
    [InlineData("FileName")]
    [InlineData("ActionName")]
    [InlineData("Password")]
    [InlineData("AllProjects")]
    [InlineData("function")]
    [InlineData("Arguments")]
    public void EvenKnownControlParameters_AreRejected(string parameter)
    {
        var item = ActionCatalogReader.Create("XViewAction", "ui", "DETAIL", null, null, [JsonSerializer.SerializeToElement(new { name = parameter })], null, "fixture");
        var catalog = new ActionCatalogData([item], null, []);
        Assert.Throws<BridgeCallException>(() => ActionTryTools.Check(catalog, "Scratch", item.Name, item.Type, JsonSerializer.SerializeToElement(new Dictionary<string, string> { [parameter] = "safe-looking" })));
    }

    [Fact]
    public void ExcludedUnknownAndWhitelistedActions_CannotBeTried()
    {
        foreach (var (name, type) in new[] { ("unknown", (string?)null), ("XHiddenAction", null), ("changelayer", null), ("XViewAction", "OTHER") })
        {
            var items = _catalog.Items.Concat(ActionCatalogReader.Official()).ToArray();
            Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeCallException>(() => ActionTryTools.Check(new(items, null, []), "Scratch", name, type, default)).Error.Reason);
        }
    }

    [Fact]
    public void LocalCaseAlias_CannotBypassOfficialExclusion()
    {
        var alias = ActionCatalogReader.Create("xhIDDENaCTION", "ui", null, null, null, [], null, "fixture");
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeCallException>(() => ActionTryTools.Check(
            new([.. _catalog.Items, alias], null, []), "Scratch", alias.Name, null, default)).Error.Reason);
    }

    [Fact]
    public void DangerousType_CannotBypassActionNameExclusions()
    {
        var row = ActionCatalogReader.Create("XViewAction", "ui", "PRINT", null, null, [], null, "fixture");
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeCallException>(() => ActionTryTools.Check(
            new([row], null, []), "Scratch", row.Name, row.Type, default)).Error.Reason);
    }

    [Theory]
    [InlineData("{}", false)]
    [InlineData("{\"actionTry\":\"off\"}", false)]
    [InlineData("{\"actionTry\":\"on\"}", true)]
    [InlineData("{\"actionTry\":true}", null)]
    [InlineData("{\"actionTry\":1}", null)]
    [InlineData("{\"actionTry\":\"yes\"}", null)]
    public void Config_IsExplicitAndDefaultsOff(string json, bool? enabled)
    {
        var config = HostConfig.Parse(json, new(@"D:\test"));
        if (enabled is null) { Assert.Null(config.Config); }
        else { Assert.Equal(enabled.Value, config.Config!.ActionTry == ActionRunSwitch.On); }
    }

    [Theory]
    [InlineData(false, false, 90)]
    [InlineData(true, false, 91)]
    [InlineData(false, true, 25)]
    [InlineData(true, true, 25)]
    public void RegistrationAndUsage_AreFullAndOptIn(bool enabled, bool core, int count)
    {
        var services = new ServiceCollection();
        services.AddLogging(l => l.ClearProviders());
        var config = HostConfig.Defaults(new(@"D:\test")) with { ActionTry = enabled ? ActionRunSwitch.On : ActionRunSwitch.Off };
        services.AddSingleton(config);
        var profile = new ToolProfileChoice(core ? ToolProfile.Core : ToolProfile.Full);
        services.AddSingleton(profile);
        services.AddEplanMcp();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        // HTTP sessions reconfigure collections holding the same usage tool instance.
        for (int session = 0; session < 3; session++)
        {
            ActionTrySchema.Configure(options, enabled && !core);
            Assert.Equal(count, options.ToolCollection!.Count);
            Assert.Equal(enabled && !core, options.ToolCollection.Any(t => t.ProtocolTool.Name == ActionTryTools.Tool));
            var usage = options.ToolCollection.First(t => t.ProtocolTool.Name == UsageTools.UsageTool).ProtocolTool;
            Assert.Equal(enabled && !core, usage.InputSchema.GetProperty("properties").GetProperty("tool").GetProperty("enum").EnumerateArray().Any(n => n.GetString() == ActionTryTools.Tool));
        }
        Assert.Equal(enabled && !core, UsageTools.ToolUsage(ActionTryTools.Tool, config: config, profile: profile).IsError != true);
    }

    private static ActionTryArgs Check(string json) => ActionTryTools.Check(_catalog, "Scratch", "XViewAction", "DETAIL", JsonDocument.Parse(json).RootElement);
}

[Trait("Size", "Medium")]
public sealed class ActionTryToolTests : IDisposable
{
    private readonly FakeApprovalChannel _approval;
    private readonly WriteToolHarness _harness;
    private readonly ActionTryTools _tool;
    private bool _approved = true;
    private bool _retarget;
    private bool _changed;

    public ActionTryToolTests()
    {
        _approval = new("eplan_dialog", null)
        {
            Answers = q =>
            {
                if (_retarget) { q.Plan.ProjectPath = Path.Combine(_harness!.Locations.Root, "real"); }
                return _approved ? ApprovalDecision.Granted : ApprovalDecision.Denied;
            },
        };
        _harness = new(_approval);
        string bin = Directory.CreateDirectory(Path.Combine(_harness.Locations.Root, "Bin")).FullName;
        File.WriteAllText(Path.Combine(bin, "MFTools.xml"), "<Ribbon><Button action='XViewAction /TYPE:DETAIL /Factor:1' text='View'/></Ribbon>");
        var reader = new ActionCatalogReader(() => (bin, "2026.0.3.25421"));
        _tool = new(reader, _harness.Pipeline, HostConfig.Defaults(_harness.Locations) with { ActionTry = ActionRunSwitch.On });
        _harness.Bridge.Handlers[OperationNames.ActionTryPlan] = value =>
        {
            var args = (ActionTryArgs)value!;
            var plan = _harness.Plan([new() { Action = "try", Kind = "action", Name = args.Action, NewValue = JsonSerializer.Serialize(args.Parameters, HostJson.Options) }]);
            plan.Details = new() { ["unverified"] = "true", ["effects"] = "unverified; TYPE=DETAIL", ["actionRun"] = JsonSerializer.Serialize(Data(false), HostJson.Options) };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.ActionTryExecute] = _ => Data(true);
    }
    public void Dispose() => _harness.Dispose();
    private Task<CallToolResult> Run(bool dry = false) => _tool.TryAsync("Scratch", "XViewAction", "DETAIL", JsonSerializer.SerializeToElement(new { Factor = 1.25 }), dry, cancellationToken: TestContext.Current.CancellationToken);
    private ActionRunData Data(bool after) => new()
    {
        Before = new() { ["count:pages"] = "1", ["file:Design.eod"] = "10\t2026-10-30T00:00:00Z" },
        After = after ? new() { ["count:pages"] = "1", ["file:Design.eod"] = _changed ? "20\t2026-10-30T00:00:01Z" : "10\t2026-10-30T00:00:00Z" } : null,
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EveryCallInScratch_NeedsApprovalAndAuditsUnverifiedEvidence(bool changed)
    {
        _changed = changed;
        for (int i = 0; i < 2; i++)
        {
            var result = WriteToolHarness.Structured(await Run());
            Assert.Equal("committed", result.GetProperty("execution").GetString());
            Assert.True(result.GetProperty("unverified").GetBoolean());
            Assert.Equal(changed, result.GetProperty("changed").GetBoolean());
            Assert.Equal("10\t2026-10-30T00:00:00Z", result.GetProperty("before").GetProperty("file:Design.eod").GetString());
        }
        Assert.Equal(2, _approval.Questions.Count);
        Assert.Equal(2, _harness.Bridge.Writes.Count);
        Assert.All(_harness.Bridge.Writes, w => Assert.Equal(TimeSpan.FromSeconds(30), w.Limit));
        ApprovalAskArgs text = ApprovalTexts.For(_approval.Questions[0], CultureInfo.GetCultureInfo("en-US"));
        Assert.Contains("XViewAction", text.Changes[0], StringComparison.Ordinal);
        Assert.Contains("Factor=1.25", text.Changes[0], StringComparison.Ordinal);
        Assert.Contains("TYPE=DETAIL", text.Changes[0], StringComparison.Ordinal);
        Assert.Contains("unverified", text.Changes[0], StringComparison.Ordinal);
        string[] lines = File.ReadAllLines(Directory.GetFiles(_harness.Locations.Audit, "*.jsonl").Single());
        var records = lines.Select(l => JsonNode.Parse(l)!).ToArray();
        Assert.All(records, r => Assert.True(r["unverified"]!.GetValue<bool>()));
        Assert.Contains(records, r => r["kind"]?.GetValue<string>() == "execution_result" && r["execution"]?.GetValue<string>() == "committed");
        Assert.All(_harness.Bridge.Calls.Where(c => c.Operation == OperationNames.ActionTryExecute), c => Assert.Equal("f1", ((ActionTryArgs)c.Args!).Fingerprint));
    }

    [Fact]
    public async Task DryRun_DoesNotAskOrExecute_AndOutsideScratchStillDenies()
    {
        Assert.Equal("not_started", WriteToolHarness.Structured(await Run(true)).GetProperty("execution").GetString());
        Assert.Empty(_approval.Questions);
        _harness.ProjectPath = Path.Combine(_harness.Locations.Root, "real");
        Assert.Equal(ErrorReason.PolicyDenied, WriteToolHarness.Error(await Run(true)).GetProperty("reason").GetString());
        Assert.Equal(ErrorReason.PolicyDenied, WriteToolHarness.Error(await Run()).GetProperty("reason").GetString());
        Assert.Empty(_approval.Questions);
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task ScratchIsRecheckedAfterApproval()
    {
        _retarget = true;
        var error = WriteToolHarness.Error(await Run());
        Assert.Equal(ErrorReason.PolicyDenied, error.GetProperty("reason").GetString());
        Assert.Equal("not_started", error.GetProperty("execution").GetString());
        Assert.Single(_approval.Questions);
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task RefusalAndAuditFailure_NeverExecute()
    {
        _approved = false;
        Assert.Equal(ErrorReason.ApprovalDenied, WriteToolHarness.Error(await Run()).GetProperty("reason").GetString());
        _approved = true;
        Directory.Delete(_harness.Locations.Audit, true);
        File.WriteAllText(_harness.Locations.Audit, "blocked");
        Assert.Equal(ErrorReason.AuditUnavailable, WriteToolHarness.Error(await Run()).GetProperty("reason").GetString());
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task UnknownOutcome_SuspendsProjectAndInstance_WithoutResending()
    {
        _harness.Bridge.WriteFailure = new BridgeError { Reason = ErrorReason.OutcomeUnknown, Execution = ExecutionState.Unknown, Message = "A timeout or modal dialog prevented outcome recovery." };
        var error = WriteToolHarness.Error(await Run());
        Assert.Equal("unknown", error.GetProperty("execution").GetString());
        Assert.Single(_harness.Bridge.Writes);
        var instance = _harness.Bridge.Status.Handshake!.Eplan;
        var store = SuspensionStore.For(_harness.Locations);
        Assert.NotEmpty(store.Blocking(_harness.ProjectPath, instance.ProcessId, SuspensionStore.StartOf(instance.ProcessId)!.Value));
        Assert.NotEmpty(store.Blocking("another project", instance.ProcessId, SuspensionStore.StartOf(instance.ProcessId)!.Value));
    }

    [Fact]
    public async Task RecoveredCommit_DoesNotInventAfterEvidence()
    {
        _harness.Bridge.WriteRecovered = true;
        var result = WriteToolHarness.Structured(await Run());
        Assert.True(result.GetProperty("recovered").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("after").ValueKind);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("changed").ValueKind);
        Assert.Single(_harness.Bridge.Writes);
    }
}
