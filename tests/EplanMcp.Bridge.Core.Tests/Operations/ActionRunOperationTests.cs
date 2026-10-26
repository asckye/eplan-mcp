using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ActionRunOperationTests
{
    private static readonly string[] _entries =
    [
        "backup/MASTERDATA", "changelayer", "graphicallayertable/EXPORT", "partsmanagementapi/EXPORTPARTS",
        "partsmanagementapi/EXPORTMANUFACTURERS", "partsmanagementapi/EXPORTCONSTRUCTIONS", "partsmanagementapi/EXPORTCONNECTIONPATTERNS",
        "partsmanagementapi/EXPORTACCESSORYLISTS", "partsmanagementapi/EXPORTACCESSORYPLACEMENTS", "plcservice/BUSDATAEXPORT",
        "projectmanagement/EXPORTPROPERTYPLACEMENTSSCHEMAS", "XEsUserPropertiesExportAction",
        "SwitchProjectType", "synchronize/MULTILINE", "synchronize/OVERVIEW",
    ];
    private readonly FakeCatalog _catalog = new(new OpenProject { Name = "Line1", Path = @"D:\Scratch\Line1", Current = false });
    private readonly Runner _runner = new();
    private readonly Messages _messages = new();
    private readonly FakeTransactions _transactions = new();

    public ActionRunOperationTests() => _transactions.Snapshot = () =>
    {
        bool visible = _runner.Visible;
        return () => _runner.Visible = visible;
    };

    [Fact]
    public void PendingR2Rules_KeepOfficialRowsAndCannotPlanExportOrWrite()
    {
        JObject[] pending = [.. ActionRunRequest.Document()["pendingVerification"]!.OfType<JObject>()];
        Assert.Equal([75, 76, 119, 133, 155], pending.Select(rule => (int)rule["source"]!));
        foreach (JObject rule in pending)
        {
            string entry = (string)rule["entry"]!;
            Assert.Equal("pending_verification", (string?)rule["status"]);
            Assert.Equal("project", (string?)rule["scope"]);
            Assert.Equal("PROJECTNAME", (string?)rule["projectParameter"]);
            Assert.Contains("Pending S26", (string)rule["effects"]!, StringComparison.Ordinal);
            Assert.InRange((int)rule["seconds"]!, 1, 300);
            var args = new ActionRunArgs { Entry = entry, Project = "Line1", Fingerprint = "untrusted", Directory = @"D:\exports\fresh" };
            Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
            Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeErrorException>(() => ExportOperation().Execute(args)).Error.Reason);
            BridgeError denied = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
            Assert.Equal((ErrorReason.PolicyDenied, ExecutionState.NotStarted), (denied.Reason, denied.Execution));
        }
        Assert.Empty(_runner.Calls);
        Assert.Empty(_messages.Events);
        Assert.Empty(_transactions.Calls);
        JObject common = pending.Single(rule => (int)rule["source"]! == 155);
        Assert.Equal("0", (string?)common["fixed"]?["EXECUTIONMODE"]);
        Assert.Equal("0", (string?)common["fixed"]?["IMMEDIATEIMPORT"]);
        Assert.Equal("1", (string?)common["fixed"]?["COMPLETEPROJECT"]);
        Assert.Equal(JTokenType.Null, common["schemeNode"]!.Type);
        Assert.Empty(common["verifiedXmlSchemes"]!);
        Assert.Equal(".xml", (string?)common["extension"]);
        Assert.Null(pending.Single(rule => (int)rule["source"]! == 75)["fixed"]?["OVERWRITE"]);
    }

    [Fact]
    public void EmbeddedWhitelist_HasExactlyTheFifteenEntriesAndEvidence()
    {
        JObject document = ActionRunRequest.Document();
        Assert.Equal(_entries, document["entries"]!.Select(e => (string)e["entry"]!));
        Assert.Equal(2026, (int)document["verification"]!["major"]!);
        foreach (JToken entry in document["entries"]!)
        {
            Assert.InRange((int)entry["seconds"]!, 1, 300);
            Assert.NotEmpty((string)entry["effects"]!);
            Assert.NotNull(entry["source"]);
            Assert.True((string?)entry["tier"] is "write" or "export");
        }
        Assert.Equal(4, document["entries"]!.Count(e => (string?)e["tier"] == "write"));
    }

    [Theory]
    [InlineData("Schematic", "1")]
    [InlineData("Macro", "2")]
    public void SwitchProjectType_NoOpIsRefusedBeforePlanningOrWriting(string current, string target)
    {
        _runner.ProjectType = current;
        var args = new ActionRunArgs { Entry = "SwitchProjectType", Project = "Line1", Parameters = [new() { Name = "PROJECTTYPE", Value = target }] };
        BridgeError planError = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;
        Assert.Equal(ErrorReason.InvalidArgument, planError.Reason);
        Assert.Contains("PROJECTTYPE", planError.Metadata!["violations"], StringComparison.Ordinal);
        args.Fingerprint = "earlier plan";
        BridgeError executeError = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (executeError.Reason, executeError.Execution));
        Assert.Empty(_transactions.Calls);
        Assert.Equal(0, _runner.Runs);
    }

    [Theory]
    [InlineData("SwitchProjectType", "2", "Macro")]
    [InlineData("SwitchProjectType", "1", "Schematic")]
    [InlineData("synchronize/MULTILINE", null, null)]
    [InlineData("synchronize/OVERVIEW", null, null)]
    public void PromotedEntry_UsesWriteTransactionAndReadsBackAfterTheAction(string entry, string? target, string? expected)
    {
        _runner.ProjectType = target == "1" ? "Macro" : "Schematic";
        var args = new ActionRunArgs { Entry = entry, Project = "Line1" };
        if (target is not null)
        {
            args.Parameters.Add(new() { Name = "PROJECTTYPE", Value = target });
        }
        args.Fingerprint = Plan(args).Fingerprint;
        ActionRunData result = Execute(args);
        Assert.NotNull(result.After);
        Assert.Equal(expected ?? "Schematic", result.After["project:type"]);
        Assert.Equal(["Begin AI: eplan_action_run", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
        Assert.Equal(@"D:\Scratch\Line1", Get(result, "PROJECTNAME"));
        if (target is null)
        {
            Assert.Equal(entry.Split('/')[1], Get(result, "TYPE"));
        }
    }

    [Fact]
    public void BridgeSerialization_PreservesParameterNamesAndFingerprintFileNames()
    {
        var data = new ActionRunData
        {
            Parameters = Pairs(new Dictionary<string, string> { ["LAYER1"] = "EPLAN100", ["XMLFile"] = "file.xml", ["TYPE"] = "EXPORT", ["Project"] = "Line1" }),
            Before = new() { ["file:Layer.eod"] = "42\t2026-10-22T00:00:00Z" },
        };
        JToken json = BridgeJson.FromObject(data);
        Assert.Equal(["LAYER1", "XMLFile", "TYPE", "Project"], json["parameters"]!.Select(p => (string)p["name"]!));
        Assert.Equal("42\t2026-10-22T00:00:00Z", (string?)json["before"]!["file:Layer.eod"]);
        ActionRunData copy = json.ToObject<ActionRunData>(BridgeJson.Serializer)!;
        Assert.Equal(data.Parameters.Select(p => (p.Name, p.Value)), copy.Parameters.Select(p => (p.Name, p.Value)));
        Assert.Equal(data.Before, copy.Before);
    }

    [Fact]
    public void DuplicateOrNullParameterName_IsRefusedBeforeInspection()
    {
        foreach (ActionParameter? parameter in new ActionParameter?[] { new() { Name = "LAYER1", Value = "other" }, new() { Name = null!, Value = "x" }, null })
        {
            ActionRunArgs args = Layer();
            args.Parameters.Add(parameter!);
            Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
        }
        Assert.Empty(_runner.Calls);
    }

    [Theory]
    [InlineData("ProjectAction")]
    [InlineData("projectaction")]
    [InlineData("changelayer/EXPORT")]
    [InlineData("ChangeLayer")]
    [InlineData("graphicallayertable/export")]
    [InlineData("plcservice/EXPORTADDRESSOVERVIEW")]
    [InlineData("XMActionDCCommonExport")]
    [InlineData("XMExportDCArticleDataAction")]
    [InlineData("ExecuteScript")]
    [InlineData("")]
    public void UnknownEntry_IsDeniedBeforeTargetInspection(string entry)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(new ActionRunArgs { Entry = entry })).Error;
        Assert.Equal((ErrorReason.PolicyDenied, "Not in the action whitelist."), (error.Reason, error.Message));
        Assert.Empty(_runner.Calls);
    }

    [Theory]
    [InlineData(2025)]
    [InlineData(2027)]
    [InlineData(0)]
    public void UnverifiedVersion_IsDeniedBeforeTargetInspection(int major)
    {
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeErrorException>(() => Plan(Layer(), major)).Error.Reason);
        Assert.Empty(_runner.Calls);
    }

    [Theory]
    [InlineData("TYPE")]
    [InlineData("PROJECTNAME")]
    [InlineData("PRINTED1")]
    [InlineData("LAYER2")]
    [InlineData("VISIBLE2")]
    [InlineData("LAYER")]
    [InlineData("VISIBLE")]
    [InlineData("Action")]
    public void CallerSuppliedFixedOrUnknownParameter_IsInvalid(string key)
    {
        ActionRunArgs args = Layer();
        Set(args, key, "value");
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
        Assert.Empty(_runner.Calls);
    }

    [Theory]
    [InlineData("/LAYER2:test")]
    [InlineData("a\" /VISIBLE2:0")]
    [InlineData("a'b")]
    [InlineData("\n")]
    [InlineData("\u0000")]
    [InlineData("")]
    [InlineData(" ")]
    public void InjectionOrBlankText_IsInvalid(string value)
    {
        ActionRunArgs args = Layer();
        Set(args, "LAYER1", value);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
    }

    [Theory]
    [InlineData(@"..\layers.elc")]
    [InlineData(@"D:\layers.elc")]
    [InlineData(@"\\server\share\layers.elc")]
    [InlineData("sibling/layers.elc")]
    [InlineData("CON.elc")]
    [InlineData("CON .elc")]
    [InlineData("lpt0.elc")]
    [InlineData("COM¹.elc")]
    [InlineData("layers.elc.")]
    [InlineData("layers.elc ")]
    [InlineData("layers?.elc")]
    [InlineData("layers.xml")]
    [InlineData(".elc")]
    public void ExportPathTrickOrWrongExtension_IsInvalid(string file)
    {
        ActionRunArgs args = Export();
        Set(args, "EXPORTFILE", file);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public void AllArgumentViolations_AreReportedTogether()
    {
        ActionRunArgs args = Layer();
        args.Project = null;
        args.Parameters.Clear();
        Set(args, "PROJECTNAME", "bypass");
        string violations = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Metadata!["violations"];
        Assert.Contains("project", violations, StringComparison.Ordinal);
        Assert.Contains("LAYER1", violations, StringComparison.Ordinal);
        Assert.Contains("VISIBLE1", violations, StringComparison.Ordinal);
        Assert.Contains("PROJECTNAME", violations, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("EXPORTPARTS", "22001")]
    [InlineData("EXPORTMANUFACTURERS", "22900")]
    [InlineData("EXPORTCONSTRUCTIONS", "22931")]
    [InlineData("EXPORTCONNECTIONPATTERNS", "22945")]
    [InlineData("EXPORTACCESSORYLISTS", "22960")]
    [InlineData("EXPORTACCESSORYPLACEMENTS", "22970")]
    public void PartsExports_RequireExactlyTheVerifiedFilterAndFixedConverter(string type, string id)
    {
        var args = new ActionRunArgs
        {
            Entry = "partsmanagementapi/" + type,
            Parameters = Pairs(new Dictionary<string, string> { ["EXPORTFILE"] = "parts.xml", ["PROPERTYID1"] = id, ["PROPERTYVALUE1"] = "A-B.100 中文" }),
        };
        ActionRunData data = Preview(Plan(args));
        Assert.Equal(type, Get(data, "TYPE"));
        Assert.Equal("XPamExportXml", Get(data, "FORMAT"));
        Assert.Equal("A-B.100 中文", Get(data, "PROPERTYVALUE1"));
        foreach (string key in new[] { "PROPERTYID1", "PROPERTYVALUE1" })
        {
            string value = Get(args, key);
            args.Parameters.RemoveAll(p => p.Name == key);
            Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
            Set(args, key, value);
        }
        Set(args, "PROPERTYID1", "99999");
        Set(args, "PROPERTYVALUE1", "*");
        string violations = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Metadata!["violations"];
        Assert.Contains("PROPERTYID1", violations, StringComparison.Ordinal);
        Assert.Contains("PROPERTYVALUE1", violations, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PlcDcXMLExchangerUniversal", null, "bus.pbf", true)]
    [InlineData("PlcDcAMLExchangerGeneral", null, "bus.aml", true)]
    [InlineData("PlcDcAMLExchangerGeneral", "AutomationML AR APC V1.4.0", "bus.aml", true)]
    [InlineData("PlcDcXMLExchangerUniversal", "AutomationML AR APC V1.4.0", "bus.pbf", false)]
    [InlineData("PlcDcAMLExchangerGeneral", "other", "bus.aml", false)]
    [InlineData("unknown", null, "bus.aml", false)]
    [InlineData("PlcDcXMLExchangerUniversal", null, "bus.aml", false)]
    public void Plc_OnlyTheThreeVerifiedCombinationsAreAccepted(string converter, string? format, string file, bool valid)
    {
        var args = new ActionRunArgs { Entry = "plcservice/BUSDATAEXPORT", Project = "Line1", Parameters = Pairs(new Dictionary<string, string> { ["CONFIGURATIONPROJECT"] = "Phoenix1", ["CONVERTERID"] = converter, ["DESTINATIONFILE"] = file }) };
        if (format is not null)
        { Set(args, "FORMAT", format); }
        if (!valid)
        {
            Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
            return;
        }
        ActionRunData data = Preview(Plan(args));
        Assert.Equal("0", Get(data, "OVERWRITE"));
        Assert.Equal("de_DE", Get(data, "LANGUAGE"));
        Assert.Equal(@"D:\Scratch\Line1", Get(data, "PROJECTNAME"));
        Assert.Contains("cause unverified", (string)ActionRunRequest.Document()["entries"]!.Single(e => (string?)e["entry"] == args.Entry)["effects"]!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ARCHIVENAME", "archive.zw6")]
    [InlineData("ARCHIVENAME", "a b")]
    [InlineData("MDTYPE", "PROJECT")]
    [InlineData("SOURCEPATH", @"D:\other")]
    public void Backup_RejectsUnverifiedTypesNamesAndDerivedKeys(string key, string value)
    {
        var args = new ActionRunArgs { Entry = "backup/MASTERDATA", Parameters = Pairs(new Dictionary<string, string> { ["MDTYPE"] = "ARTICLES", ["ARCHIVENAME"] = "parts_1" }) };
        Set(args, key, value);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
    }

    [Fact]
    public void SystemProjectAndProjectWithoutProject_AreInvalid()
    {
        var system = new ActionRunArgs { Entry = "backup/MASTERDATA", Project = "Line1", Parameters = Pairs(new Dictionary<string, string> { ["MDTYPE"] = "LANGUAGES", ["ARCHIVENAME"] = "languages" }) };
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(system)).Error.Reason);
        ActionRunArgs project = Layer();
        project.Project = "";
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(project)).Error.Reason);
    }

    [Fact]
    public void Plan_IsReadOnlyAndKeepsLegalTextExactly()
    {
        ActionRunArgs args = Layer();
        Set(args, "LAYER1", "  Layer 中文 / text  ");
        WritePlan plan = Plan(args);
        Assert.Equal("  Layer 中文 / text  ", Get(Preview(plan), "LAYER1"));
        Assert.Equal("write", plan.Details!["tier"]);
        Assert.Equal(("Line1", @"D:\Scratch\Line1"), (plan.Project, plan.ProjectPath));
        Assert.Single(plan.Changes);
        Assert.Equal(0, _runner.Runs);
        Assert.Empty(_transactions.Calls);
        Assert.Empty(_messages.Events);
    }

    [Fact]
    public void Fingerprint_IsIndependentOfDictionaryInsertionOrder_AndIncludesParameters()
    {
        ActionRunArgs first = Layer();
        string fingerprint = Plan(first).Fingerprint;
        first.Parameters.Reverse();
        Assert.Equal(fingerprint, Plan(first).Fingerprint);
        Set(first, "VISIBLE1", "1");
        Assert.NotEqual(fingerprint, Plan(first).Fingerprint);
    }

    [Fact]
    public void Export_PreparesFolderBeforeBookmark_AndNeverUsesATransaction()
    {
        _runner.OnRun = () => _messages.Items.Add(new() { Index = "S000002", Text = "action note", Level = 2 });
        ActionRunOperation operation = ExportOperation();
        ActionRunData result = (ActionRunData)operation.Execute(Export())!;
        Assert.Equal(["prepare", "bookmark", "messages"], _messages.Events);
        Assert.Equal(["S000002: action note"], result.EplanMessages);
        Assert.Equal(1, result.EplanMessageCount);
        Assert.Equal(@"D:\exports\fresh\layers.elc", Get(result, "EXPORTFILE"));
        Assert.Equal("graphicallayertable", _runner.LastAction);
        Assert.Empty(_transactions.Calls);
        Assert.NotNull(result.After);
    }

    [Fact]
    public void Export_WithWriteEntry_IsDeniedBeforeFolderPreparation()
    {
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeErrorException>(() => ExportOperation().Execute(Layer())).Error.Reason);
        Assert.Empty(_messages.Events);
    }

    [Theory]
    [InlineData("ARTICLES")]
    [InlineData("LANGUAGES")]
    public void Backup_RunKeepsArchiveStemAndUsesOnlyThePreparedDestination(string type)
    {
        var args = new ActionRunArgs { Entry = "backup/MASTERDATA", Parameters = Pairs(new Dictionary<string, string> { ["MDTYPE"] = type, ["ARCHIVENAME"] = "Library_1" }) };
        ActionRunData data = (ActionRunData)ExportOperation().Execute(args)!;
        Assert.Equal("backup", _runner.LastAction);
        Assert.Equal("MASTERDATA", Get(data, "TYPE"));
        Assert.Equal("Library_1", Get(data, "ARCHIVENAME"));
        Assert.Equal(@"D:\exports\fresh", Get(data, "DESTINATIONPATH"));
        Assert.Equal(type, Get(data, "MDTYPE"));
    }

    [Theory]
    [InlineData("projectmanagement/EXPORTPROPERTYPLACEMENTSSCHEMAS", "FILENAME", "PROJECTNAME", "placements.xml")]
    [InlineData("XEsUserPropertiesExportAction", "XMLFile", "Project", "properties.xml")]
    public void PropertyExports_KeepTheVerifiedParameterSpelling(string entry, string fileParameter, string projectParameter, string file)
    {
        var args = new ActionRunArgs { Entry = entry, Project = "Line1", Parameters = Pairs(new Dictionary<string, string> { [fileParameter] = file }) };
        ActionRunData data = (ActionRunData)ExportOperation().Execute(args)!;
        Assert.Equal(@"D:\Scratch\Line1", Get(data, projectParameter));
        Assert.Equal(Path.Combine(@"D:\exports\fresh", file), Get(data, fileParameter));
        Assert.Equal(entry.Contains('/', StringComparison.Ordinal), data.Parameters.Any(p => p.Name == "TYPE"));
    }

    [Fact]
    public void ReadOnlyProject_WriteIsRejectedBeforeInspection()
    {
        var catalog = new FakeCatalog(new OpenProject { Name = "Line1", Path = @"D:\Scratch\Line1", ReadOnly = true });
        var operation = new ActionRunPlanOperation(2026, catalog, _runner);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => operation.Execute(Layer())).Error.Reason);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public void UnreadableActionMessages_RollBackWithoutClaimingSuccess()
    {
        ActionRunArgs args = Layer();
        args.Fingerprint = Plan(args).Fingerprint;
        _runner.OnRun = () => _messages.FailRead = true;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.True(_runner.Visible);
    }

    [Fact]
    public void Write_WithExportEntry_IsDeniedWithoutATransaction()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(Export())).Error;
        Assert.Equal((ErrorReason.PolicyDenied, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_transactions.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrStaleFingerprint_IsNotStarted(bool stale)
    {
        ActionRunArgs args = Layer();
        args.Fingerprint = stale ? Plan(args).Fingerprint : null;
        _runner.Visible = false;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
        Assert.Equal((stale ? ErrorReason.PlanStale : ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_transactions.Calls);
        Assert.Equal(0, _runner.Runs);
    }

    [Fact]
    public void Write_CommitsAfterReadbackAndReportsUndoLimitations()
    {
        ActionRunArgs args = Layer();
        args.Fingerprint = Plan(args).Fingerprint;
        ActionRunData data = Execute(args);
        Assert.Equal("1", data.Before["layer:visible"]);
        Assert.Equal("0", data.After!["layer:visible"]);
        Assert.Equal(["Begin AI: eplan_action_run", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
        Assert.Contains("undo", data.Warning, StringComparison.Ordinal);
        Assert.Contains("not been checked item by item", data.Warning, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("exception")]
    [InlineData("error")]
    [InlineData("fatal")]
    [InlineData("readback")]
    public void FailedActionOrReadback_RollsBack(string mode)
    {
        ActionRunArgs args = Layer();
        args.Fingerprint = Plan(args).Fingerprint;
        _runner.Done = mode != "false";
        _runner.OnRun = () =>
        {
            if (mode == "exception")
            { throw new InvalidOperationException("local path must stay private"); }
            if (mode is "error" or "fatal")
            { _messages.Items.Add(new() { Text = "failure", Level = mode == "error" ? 4 : 5 }); }
            if (mode == "readback")
            { _runner.FailInspection = true; }
        };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack, false), (error.Reason, error.Execution, error.Retryable));
        Assert.Equal(["Begin AI: eplan_action_run", "CloseUndo", "Rollback", "Dispose"], _transactions.Calls);
        Assert.True(_runner.Visible);
        Assert.DoesNotContain("local path", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorBeyondDisplayedMessages_StillFailsAndReturnsOnlyTwentyMessages()
    {
        _runner.OnRun = () =>
        {
            _messages.Items.AddRange(Enumerable.Range(0, 20).Select(i => new SystemMessageRecord { Text = "note", Level = 2 }));
            _messages.Items.Add(new() { Text = "last is error", Level = 4 });
        };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => ExportOperation().Execute(Export())).Error;
        Assert.Equal(ErrorReason.EplanOperationFailed, error.Reason);
        Assert.Equal(20, JArray.Parse(error.Metadata!["eplanMessages"]).Count);
    }

    [Fact]
    public void WarningsBeyondTwenty_AreCountedWithoutLosingTheFullFingerprint()
    {
        _runner.OnRun = () => _messages.Items.AddRange(Enumerable.Range(0, 25).Select(i => new SystemMessageRecord { Text = "note", Level = 2 }));
        ActionRunData result = (ActionRunData)ExportOperation().Execute(Export())!;
        Assert.Equal(25, result.EplanMessageCount);
        Assert.Equal(20, result.EplanMessages.Count);
        Assert.Equal(33, result.Before.Count);
        Assert.Equal(33, result.After!.Count);
    }

    [Fact]
    public void CancelAfterAction_RollsBackAtTheSafePoint()
    {
        ActionRunArgs args = Layer();
        args.Fingerprint = Plan(args).Fingerprint;
        var context = new OperationContext("r1", write: true, progress: null);
        _runner.OnRun = context.Cancel;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => context.Run(() => Execute(args))).Error;
        Assert.Equal((ErrorReason.Cancelled, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.True(_runner.Visible);
    }

    [Fact]
    public void FailedRollback_IsUnknown()
    {
        ActionRunArgs args = Layer();
        args.Fingerprint = Plan(args).Fingerprint;
        _transactions.FailAt = "Rollback";
        _runner.Done = false;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown, false), (error.Reason, error.Execution, error.Retryable));
    }

    [Fact]
    public void Router_RecordsCommittedOutcomeAndDoesNotRepeatTheAction()
    {
        ActionRunArgs args = Layer();
        args.Fingerprint = Plan(args).Fingerprint;
        var router = new OperationRouter([WriteOperation()], new InlineDispatcher(), null, new ListLog(), TimeSpan.FromSeconds(10));
        JToken json = BridgeJson.FromObject(args);
        router.Invoke(OperationNames.ActionRunExecute, json, "r1");
        Assert.Equal(OutcomeStatus.Committed, (string)router.Invoke(OperationNames.OutcomeGet, new JObject { ["requestId"] = "r1" }, "outcome")["status"]!);
        Assert.Throws<BridgeErrorException>(() => router.Invoke(OperationNames.ActionRunExecute, json, "r1"));
        Assert.Equal(1, _runner.Runs);
    }

    private WritePlan Plan(ActionRunArgs args, int major = 2026) => (WritePlan)new ActionRunPlanOperation(major, _catalog, _runner).Execute(args)!;
    private ActionRunData Execute(ActionRunArgs args) => (ActionRunData)WriteOperation().Execute(args)!;
    private ActionRunExecuteOperation WriteOperation() => new(2026, _catalog, _runner, _messages, _transactions);
    private ActionRunOperation ExportOperation() => new(2026, _catalog, _runner, _messages, _ =>
    {
        _messages.Events.Add("prepare");
        _messages.Items.Add(new() { Text = "preparation failure unrelated to action", Level = 4 });
        return @"D:\exports\fresh";
    });
    private static List<ActionParameter> Pairs(IEnumerable<KeyValuePair<string, string>> values) => [.. values.Select(p => new ActionParameter { Name = p.Key, Value = p.Value })];
    private static string Get(ActionRunData data, string name) => data.Parameters.Single(p => p.Name == name).Value;
    private static string Get(ActionRunArgs args, string name) => args.Parameters.Single(p => p.Name == name).Value;
    private static void Set(ActionRunArgs args, string name, string value)
    {
        args.Parameters.RemoveAll(p => p.Name == name);
        args.Parameters.Add(new() { Name = name, Value = value });
    }
    private static ActionRunData Preview(WritePlan plan) => JsonConvert.DeserializeObject<ActionRunData>(plan.Details!["actionRun"])!;
    private static ActionRunArgs Layer() => new() { Entry = "changelayer", Project = "Line1", Parameters = Pairs(new Dictionary<string, string> { ["LAYER1"] = "EPLAN100", ["VISIBLE1"] = "0" }) };
    private static ActionRunArgs Export() => new() { Entry = "graphicallayertable/EXPORT", Project = "Line1", Parameters = Pairs(new Dictionary<string, string> { ["EXPORTFILE"] = "layers.elc" }) };

    private sealed class Messages : ISystemMessageReader
    {
        public bool FailRead { get; set; }
        public List<SystemMessageRecord> Items { get; } = [];
        public List<string> Events { get; } = [];
        public IReadOnlyList<SystemMessageRecord> ReadAll()
        {
            if (FailRead)
            { throw new InvalidOperationException("message read failed"); }
            Events.Add(Events.Contains("bookmark", StringComparer.Ordinal) ? "messages" : "bookmark");
            return Items.ToArray();
        }
    }

    private sealed class Runner : IActionRunner
    {
        public string ProjectType { get; set; } = "Schematic";
        public bool Visible { get; set; } = true;
        public bool Done { get; set; } = true;
        public bool FailInspection { get; set; }
        public int Runs { get; private set; }
        public string? LastAction { get; private set; }
        public List<string> Calls { get; } = [];
        public Action? OnRun { get; set; }
        public ActionRunData Inspect(string entry, string? projectPath, IReadOnlyDictionary<string, string> parameters)
        {
            Calls.Add("Inspect");
            if (FailInspection)
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Readback failed."); }
            var state = Enumerable.Range(0, 30).ToDictionary(i => "file:" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), _ => "1\t2026-10-22T00:00:00Z");
            state["project:type"] = ProjectType;
            state["count:functions"] = "2876";
            state["layer:visible"] = Visible ? "1" : "0";
            return new ActionRunData { Target = projectPath ?? "configured-file-library", Before = state, Parameters = Pairs(parameters) };
        }
        public bool Run(string action, IReadOnlyDictionary<string, string> parameters)
        {
            Calls.Add("Run");
            Runs++;
            LastAction = action;
            if (action == "changelayer")
            { Visible = parameters["VISIBLE1"] == "1"; }
            if (action == "SwitchProjectType")
            {
                ProjectType = parameters["PROJECTTYPE"] == "1" ? "Schematic" : "Macro";
            }
            OnRun?.Invoke();
            return Done;
        }
    }
}
