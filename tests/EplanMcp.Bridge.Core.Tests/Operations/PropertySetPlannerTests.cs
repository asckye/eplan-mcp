using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PropertySetPlannerTests
{
    internal const string Project = "resolved-project";
    internal static SnapshotObject Function(long oid = 1, bool main = true) => new()
    {
        Kind = "function",
        Type = 4,
        Oid = oid,
        Name = "=AP-K1",
        Page = "=AP/1",
        Main = main,
        Category = "Coil",
        Pos = "1,2",
        Parts = ["part"],
    };

    internal static PropertySetChange Change(string text = "输送; @电机", string? language = "zh_CN") => new()
    {
        Target = new PropertyTarget(deviceTag: "=AP-K1"),
        Property = new PropertySelector(propertyName: "FUNC_TEXT"),
        Value = new PropertyTypedValue("multi_language", text: text),
        Language = language,
    };

    internal static FakePropertyReadAdapter Adapter()
    {
        var adapter = new FakePropertyReadAdapter
        {
            ObjectType = "Function",
        };
        var property = new FakeProperty
        {
            Metadata = new PropertyDefinitionMetadata
            {
                ValueType = "multi_language",
                IsIndexed = false,
                IsInternal = false,
                IsReadOnly = false,
                IsNamePart = false,
                IsPropertyLicensed = true,
            },
        };
        property.Texts.Add("zh_CN", "旧文本");
        property.Texts.Add("de_DE", "Antrieb");
        property.Texts.Add("??_??", "independent");
        adapter.Properties.Add(20011, property);
        return adapter;
    }

    internal static PropertySetPlan Plan(FakePropertyReadAdapter adapter, PropertySetChange? change = null,
        SnapshotObject[]? functions = null, int version = 2026) => new PropertySetPlanner(adapter).Plan(
            Project, version, [change ?? Change()], functions ?? [Function()], ["zh_CN", "de_DE"], CancellationToken.None);

    [Theory]
    [InlineData("中文")]
    [InlineData("")]
    [InlineData("de_DE@text;en_US@literal;")]
    [InlineData(" text \n\t")]
    public void Plan_LiteralLanguageEdit_PreservesOtherTranslationsAndAuxiliaries(string text)
    {
        FakePropertyReadAdapter adapter = Adapter();
        PropertySetPlan plan = Plan(adapter, Change(text), [Function(), Function(2, false)]);

        PlannedChange change = Assert.Single(plan.Plan.Changes);
        Assert.Equal(("旧文本", text), (change.OldValue, change.NewValue));
        Assert.Equal(2, plan.Guards.Count);
        PropertySetGuard main = Assert.Single(plan.Guards, g => g.Main);
        Assert.Equal(text, PropertyValueComparison.TextIn(main.After, "zh_CN"));
        Assert.Equal("Antrieb", PropertyValueComparison.TextIn(main.After, "de_DE"));
        Assert.Equal("independent", PropertyValueComparison.TextIn(main.After, "??_??"));
        PropertySetGuard auxiliary = Assert.Single(plan.Guards, g => !g.Main);
        Assert.Equal(auxiliary.Before.Select(t => (t.Language, t.Text)), auxiliary.After.Select(t => (t.Language, t.Text)));
        Assert.Equal(ExecutionState.NotStarted, plan.Execution);
        Assert.Equal(2, adapter.Opens);
        Assert.Equal(2, adapter.PropertyOpens);
        Assert.DoesNotContain("display", adapter.Events);
        adapter.AssertDisposed();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plan_UnchangedOrMissingEmptyLanguage_IsNoOpWithoutFallback(bool missing)
    {
        FakePropertyReadAdapter adapter = Adapter();
        if (missing)
        {
            adapter.Properties[20011].Texts.Remove("zh_CN");
        }

        PropertySetPlan plan = Plan(adapter, Change(missing ? "" : "旧文本"));

        Assert.Empty(plan.Plan.Changes);
        Assert.Equal(3 - (missing ? 1 : 0), plan.Guards[0].After.Count);
        Assert.Equal(ExecutionState.NotStarted, plan.Execution);
        adapter.AssertDisposed();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("??_??")]
    [InlineData("en_US")]
    [InlineData("zh-CN")]
    [InlineData("de_DE;zh_CN")]
    public void Plan_MissingInvalidOrNewProjectLanguage_ReportsLanguagePointer(string? language)
    {
        FakePropertyReadAdapter adapter = Adapter();
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Plan(adapter, Change(language: language)));

        AssertViolation(error, ErrorReason.InvalidArgument, "/changes/0/language");
        Assert.Equal(0, adapter.Opens);
    }

    [Theory]
    [InlineData(20024)]
    [InlineData(3012)]
    [InlineData(2000)]
    public void Plan_OutOfWhitelistProperty_IsRefusedBeforeReading(int id)
    {
        FakePropertyReadAdapter adapter = Adapter();
        PropertySetChange change = Change();
        change.Property = new PropertySelector(propertyId: id);

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Plan(adapter, change));

        Assert.Equal(ErrorReason.PolicyDenied, error.Error.Reason);
        Assert.Equal(0, adapter.Opens);
        Assert.Equal(ExecutionState.NotStarted, error.Error.Execution);
    }

    [Theory]
    [InlineData(2025)]
    [InlineData(2027)]
    public void Plan_UnverifiedVersion_IsRefused(int version)
    {
        FakePropertyReadAdapter adapter = Adapter();
        AssertViolation(Assert.Throws<BridgeErrorException>(() => Plan(adapter, version: version)),
            ErrorReason.PolicyDenied, "/changes/0/property");
        Assert.Equal(0, adapter.Opens);
    }

    [Theory]
    [InlineData("Project")]
    [InlineData("Page")]
    [InlineData("Connection")]
    [InlineData("Auxiliary")]
    public void Plan_OutOfScopeTarget_IsRefused(string kind)
    {
        FakePropertyReadAdapter adapter = Adapter();
        PropertySetChange change = Change();
        SnapshotObject obj = Function(main: kind != "Auxiliary");
        change.Target = kind switch
        {
            "Project" => new PropertyTarget(projectObject: true),
            "Page" => new PropertyTarget(page: "=AP/1"),
            _ => new PropertyTarget(locator: Locator(obj)),
        };
        if (kind == "Connection")
        {
            change.Target.Locator!.Kind = "connection";
        }

        Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeErrorException>(() => Plan(adapter, change, [obj])).Error.Reason);
        Assert.Equal(0, adapter.Opens);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("page")]
    [InlineData("category")]
    [InlineData("position")]
    [InlineData("project")]
    [InlineData("oid")]
    public void Plan_StaleLocator_IsRefusedWithTargetPointer(string field)
    {
        FakePropertyReadAdapter adapter = Adapter();
        ObjectLocator locator = Locator(Function());
        switch (field)
        {
            case "name":
                locator.Name += "-changed";
                break;
            case "page":
                locator.Page += "-changed";
                break;
            case "category":
                locator.Category += "-changed";
                break;
            case "position":
                locator.Pos = "9,9";
                break;
            case "project":
                locator.Project = "other-project";
                break;
            case "oid":
                locator.Oid++;
                break;
        }

        PropertySetChange change = Change();
        change.Target = new PropertyTarget(locator: locator);
        AssertViolation(Assert.Throws<BridgeErrorException>(() => Plan(adapter, change)), ErrorReason.PlanStale, "/changes/0/target");
        Assert.Equal(0, adapter.Opens);
    }

    [Theory]
    [InlineData("opaque")]
    [InlineData("indexed")]
    [InlineData("internal")]
    [InlineData("read_only")]
    [InlineData("name_part")]
    [InlineData("computed")]
    [InlineData("unknown")]
    [InlineData("subtype")]
    public void Plan_RuntimeDefinitionDrift_RefusesWithoutReadingValue(string drift)
    {
        FakePropertyReadAdapter adapter = Adapter();
        PropertyDefinitionMetadata metadata = adapter.Properties[20011].Metadata;
        switch (drift)
        {
            case "opaque":
                metadata.ValueType = "opaque";
                break;
            case "indexed":
                metadata.IsIndexed = true;
                break;
            case "internal":
                metadata.IsInternal = true;
                break;
            case "read_only":
                metadata.IsReadOnly = true;
                break;
            case "name_part":
                metadata.IsNamePart = true;
                break;
            case "computed":
                metadata.IsComputed = true;
                break;
            case "unknown":
                metadata.IsInternal = null;
                break;
            case "subtype":
                adapter.ObjectType = "Terminal";
                break;
        }

        Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeErrorException>(() => Plan(adapter)).Error.Reason);
        Assert.Empty(adapter.ValueRequests);
        adapter.AssertDisposed();
    }

    [Fact]
    public void Plan_UnreviewedRuntimeTarget_RefusesBeforePropertyAccessWithNotStarted()
    {
        FakePropertyReadAdapter adapter = Adapter();
        adapter.ObjectType = "UnreviewedFunction";
        PropertyReadData read = Assert.IsType<PropertyReadData>(new PropertyReadOperation(adapter).Execute(new PropertyReadArgs
        {
            ProjectPath = Project,
            Target = new PropertyTarget(locator: Locator(Function())),
            Properties = [new PropertySelector(propertyId: 20011)],
            Detailed = true,
        }));
        PropertyReadRow row = Assert.Single(read.Items);
        Assert.Equal(("read_refused", "unreviewed"), (row.Status, row.Reason));

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Plan(adapter));

        AssertViolation(error, ErrorReason.PolicyDenied, "/changes");
        Assert.Equal(0, adapter.PropertyOpens);
        Assert.Empty(adapter.ValueRequests);
        Assert.Equal(0, adapter.StoredReads);
        adapter.AssertDisposed();
    }

    [Fact]
    public void Plan_LicenceMissing_IsRefusedAndDisposesScope()
    {
        FakePropertyReadAdapter adapter = Adapter();
        adapter.Properties[20011].Metadata.IsPropertyLicensed = false;
        Assert.Equal(ErrorReason.LicenceMissing, Assert.Throws<BridgeErrorException>(() => Plan(adapter)).Error.Reason);
        adapter.AssertDisposed();
    }

    [Theory]
    [InlineData("translation")]
    [InlineData("auxiliary")]
    [InlineData("parts")]
    [InlineData("position")]
    [InlineData("request")]
    public void RequireCurrent_ChangedRawValueIdentityOrDependency_IsStale(string change)
    {
        FakePropertyReadAdapter before = Adapter();
        FakePropertyReadAdapter after = Adapter();
        SnapshotObject obj = Function();
        PropertySetChange request = Change();
        PropertySetPlan approved = Plan(before);
        SnapshotObject[] functions = [obj];
        switch (change)
        {
            case "translation":
                after.Properties[20011].Texts["de_DE"] = new string('x', 600) + "\u202e";
                break;
            case "auxiliary":
                functions = [obj, Function(2, false)];
                break;
            case "parts":
                obj.Parts = ["other"];
                break;
            case "position":
                obj.Pos = "2,3";
                break;
            case "request":
                request.Value = new("multi_language", text: "other");
                break;
        }

        PropertySetPlan current = Plan(after, request, functions);
        AssertViolation(Assert.Throws<BridgeErrorException>(() => PropertySetPlanner.RequireCurrent(approved, current)),
            ErrorReason.PlanStale, "/changes");
        Assert.Equal(3, approved.Guards[0].Before.Count);
        before.AssertDisposed();
        after.AssertDisposed();
    }

    [Fact]
    public void Plan_DuplicateAliasesAndLanguages_ReportsOriginalChangePointer()
    {
        FakePropertyReadAdapter adapter = Adapter();
        PropertySetChange second = Change();
        second.Target = new PropertyTarget(locator: Locator(Function()));
        second.Property = new PropertySelector(propertyId: 20011);
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => new PropertySetPlanner(adapter).Plan(
            Project, 2026, [Change(), second], [Function()], ["zh_CN"], CancellationToken.None));
        AssertViolation(error, ErrorReason.InvalidArgument, "/changes/1");
        Assert.Equal(0, adapter.Opens);
    }

    [Fact]
    public void Plan_TwoLanguageEdits_ShareOneListAndMergeCompleteText()
    {
        FakePropertyReadAdapter adapter = Adapter();
        PropertySetPlan plan = new PropertySetPlanner(adapter).Plan(Project, 2026,
            [Change("新文本"), Change("Motor", "de_DE")], [Function()], ["zh_CN", "de_DE"], CancellationToken.None);
        Assert.Equal(2, plan.Plan.Changes.Count);
        Assert.Equal("新文本", PropertyValueComparison.TextIn(plan.Guards[0].After, "zh_CN"));
        Assert.Equal("Motor", PropertyValueComparison.TextIn(plan.Guards[0].After, "de_DE"));
        Assert.Equal(1, adapter.Opens);
        adapter.AssertDisposed();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plan_LengthLimit_UsesProductAndRuntimeLimits(bool runtime)
    {
        FakePropertyReadAdapter adapter = Adapter();
        if (runtime)
        {
            adapter.Properties[20011].Metadata.MaximumLength = 2;
        }

        AssertViolation(Assert.Throws<BridgeErrorException>(() => Plan(adapter, Change(new string('字', runtime ? 3 : 501)))),
            ErrorReason.InvalidArgument, "/changes/0/value");
    }

    [Fact]
    public void Plan_CancelDuringRawTextRead_DisposesAllResources()
    {
        FakePropertyReadAdapter adapter = Adapter();
        using var cancellation = new CancellationTokenSource();
        adapter.OnAccess = stage =>
        {
            if (stage == "text")
            {
                cancellation.Cancel();
            }
        };
        Assert.Throws<OperationCanceledException>(() => new PropertySetPlanner(adapter).Plan(Project, 2026,
            [Change()], [Function()], ["zh_CN"], cancellation.Token));
        adapter.AssertDisposed();
    }

    internal static ObjectLocator Locator(SnapshotObject obj) => new()
    {
        Project = Project,
        Kind = obj.Kind,
        Type = obj.Type,
        Oid = obj.Oid,
        Name = obj.Name,
        Page = obj.Page,
        Category = obj.Category,
        Pos = obj.Pos,
    };

    [Fact]
    public void Plan_InvalidTypesIndexesAndUnknownNames_CollectsValueAndSelectorPointers()
    {
        FakePropertyReadAdapter adapter = Adapter();
        PropertySetChange typed = Change();
        typed.Value = new PropertyTypedValue("boolean", boolean: true);
        typed.Property = new PropertySelector(propertyId: 20011, index: 1);
        PropertySetChange unknown = Change();
        unknown.Property = new PropertySelector(propertyName: "not_a_property");
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => new PropertySetPlanner(adapter).Plan(
            Project, 2026, [typed, unknown], [Function()], ["zh_CN"], CancellationToken.None));

        AssertViolation(error, ErrorReason.InvalidArgument, "/changes/0/value");
        AssertViolation(error, ErrorReason.InvalidArgument, "/changes/0/property/index");
        AssertViolation(error, ErrorReason.InvalidArgument, "/changes/1/property/property_name");
        Assert.Equal(0, adapter.Opens);
    }

    [Fact]
    public void Plan_ForbiddenPropertyOnMissingTarget_ChecksPolicyBeforeExistence()
    {
        FakePropertyReadAdapter adapter = Adapter();
        PropertySetChange change = Change();
        change.Property = new PropertySelector(propertyId: 20024);
        AssertViolation(Assert.Throws<BridgeErrorException>(() => Plan(adapter, change, [])),
            ErrorReason.PolicyDenied, "/changes/0/property");
        Assert.Equal(0, adapter.Opens);
    }

    [Fact]
    public void Plan_RawSuffixBeyondDisplayLimitAndControls_AffectFingerprint()
    {
        FakePropertyReadAdapter adapter = Adapter();
        adapter.Properties[20011].Texts["de_DE"] = new string('x', 600) + "\u202eA";
        PropertySetPlan approved = Plan(adapter);
        adapter.Properties[20011].Texts["de_DE"] = new string('x', 600) + "\u202eB";
        PropertySetPlan current = Plan(adapter);
        Assert.Equal(new string('x', 600) + "\u202eA", PropertyValueComparison.TextIn(approved.Guards[0].Before, "de_DE"));
        Assert.Throws<BridgeErrorException>(() => PropertySetPlanner.RequireCurrent(approved, current));
    }

    [Fact]
    public void Plan_ReorderedInventoryAndStoredLanguages_IsCanonical()
    {
        FakePropertyReadAdapter before = Adapter();
        FakePropertyReadAdapter after = Adapter();
        Dictionary<string, string> texts = after.Properties[20011].Texts;
        KeyValuePair<string, string>[] reversed = texts.Reverse().ToArray();
        texts.Clear();
        foreach (var pair in reversed)
        {
            texts.Add(pair.Key, pair.Value);
        }

        PropertySetPlan approved = Plan(before, functions: [Function(), Function(2, false)]);
        PropertySetPlan current = Plan(after, functions: [Function(2, false), Function()]);
        PropertySetPlanner.RequireCurrent(approved, current);
        Assert.Equal(approved.Plan.Fingerprint, current.Plan.Fingerprint);
    }

    [Fact]
    public void Plan_EntirelyEmptyNativeValue_CanBePlannedWithoutReadingAnEmptyTextObject()
    {
        FakePropertyReadAdapter adapter = Adapter();
        adapter.Properties[20011].Empty = true;
        PropertySetPlan plan = Plan(adapter);
        Assert.True(plan.Guards[0].WasEmpty);
        Assert.Empty(plan.Guards[0].Before);
        Assert.Single(plan.Guards[0].After);
        Assert.DoesNotContain("texts.opened", adapter.Events);
        adapter.AssertDisposed();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("ambiguous")]
    public void Plan_UnresolvedBusinessAddress_RefusesWholePlan(string problem)
    {
        FakePropertyReadAdapter adapter = Adapter();
        SnapshotObject[] functions = problem == "missing" ? [] : [Function(), Function(2)];
        string reason = problem == "missing" ? ErrorReason.DeviceNotFound : ErrorReason.AmbiguousName;
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Plan(adapter, functions: functions));
        AssertViolation(error, reason, "/changes/0/target");
        if (problem == "ambiguous")
        {
            Assert.Equal(2, JArray.Parse(error.Error.Metadata!["candidates"]).Count);
        }

        Assert.Equal(0, adapter.Opens);
    }

    [Fact]
    public void Plan_MoreThanFiftyResolvedObjects_IsRefusedBeforeAnyRead()
    {
        FakePropertyReadAdapter adapter = Adapter();
        SnapshotObject[] functions = Enumerable.Range(1, 51).Select(i => Function(i)).ToArray();
        PropertySetChange[] changes = functions.Select(f =>
        {
            PropertySetChange change = Change();
            change.Target = new PropertyTarget(locator: Locator(f));
            return change;
        }).ToArray();
        AssertViolation(Assert.Throws<BridgeErrorException>(() => new PropertySetPlanner(adapter).Plan(
            Project, 2026, changes, functions, ["zh_CN"], CancellationToken.None)), ErrorReason.InvalidArgument, "/changes");
        Assert.Equal(0, adapter.Opens);
    }

    [Fact]
    public void Plan_InputOver64KiB_IsRefusedBeforeAnyRead()
    {
        FakePropertyReadAdapter adapter = Adapter();
        PropertySetChange[] changes = Enumerable.Range(0, 100).Select(_ => Change(new string('字', 500))).ToArray();
        AssertViolation(Assert.Throws<BridgeErrorException>(() => new PropertySetPlanner(adapter).Plan(
            Project, 2026, changes, [Function()], ["zh_CN"], CancellationToken.None)), ErrorReason.InvalidArgument, "");
        Assert.Equal(0, adapter.Opens);
    }

    [Fact]
    public void Plan_CompleteRawGuardOverBridgeBudget_IsRefusedWithoutTruncation()
    {
        FakePropertyReadAdapter adapter = Adapter();
        adapter.Properties[20011].Texts["de_DE"] = new string('x', 600_000);
        AssertViolation(Assert.Throws<BridgeErrorException>(() => Plan(adapter)), ErrorReason.InvalidArgument, "/changes");
        adapter.AssertDisposed();
    }

    internal static void AssertViolation(BridgeErrorException error, string reason, string path)
    {
        Assert.Equal(reason, error.Error.Reason);
        Assert.Equal(ExecutionState.NotStarted, error.Error.Execution);
        JArray violations = JArray.Parse(error.Error.Metadata!["violations"]);
        Assert.Contains(violations, v => (string?)v["path"] == path);
    }
}
