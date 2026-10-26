using System.Text.Json;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Tests;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

/// <summary>P6 B1 planning only, using existing Bridge reads on writes/LargeWrite; no write operation is invoked.</summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class PropertySetPlanningTests(LiveEplan eplan)
{
    [Fact]
    public Task B1_LargeWrite_LiteralEditsAndNoOp_PreserveTranslationsAndDesignFiles()
    {
        return CheckPlanningAsync(auxiliaryScope: false);
    }

    [Fact]
    public Task B1_LargeWrite_LiteralEditsAndNoOp_PreserveAuxiliariesAndDesignFiles()
    {
        return CheckPlanningAsync(auxiliaryScope: true);
    }

    private async Task CheckPlanningAsync(bool auxiliaryScope)
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        await LiveProperties.DescribeEnvironmentAsync(bridge, eplan.ProcessId);
        string project = await LargeWriteAsync(bridge);
        Dictionary<string, string> files = LiveSnapshots.DesignFiles(project);
        LivePlanSample sample = await LivePlanSample.ReadAsync(bridge, project);
        SnapshotObject main = await TextMainAsync(bridge, project, sample, auxiliaryScope);
        // The translation gate isolates one target; the auxiliary gate covers the complete same-name group.
        SnapshotObject[] related = auxiliaryScope
            ? sample.Snapshot.Objects("function").Where(f => f.Name == main.Name).ToArray() : [main];
        if (auxiliaryScope)
        {
            Assert.Contains(related, f => f.Main == false);
        }
        var adapter = await CaptureAsync(bridge, project, related);
        string[] languages = await LanguagesAsync(bridge, project);
        Assert.Contains("zh_CN", languages);
        string original = PropertyValueComparison.TextIn(adapter.Values[(main.Type, main.Oid)].Properties[20011].Texts
            .Select(t => new SnapshotText
            {
                Language = t.Key,
                Text = t.Value,
            }), "zh_CN");

        foreach (string text in new[] { "输送电机; @", "", "de_DE@literal;en_US@literal;", original })
        {
            PropertySetChange change = Change(main, text);
            PropertySetPlan plan = new PropertySetPlanner(adapter).Plan(project, 2026, [change], related,
                languages, TestContext.Current.CancellationToken);
            PropertySetGuard guard = Assert.Single(plan.Guards, g => g.Target.Oid == main.Oid && g.Target.Type == main.Type);
            Assert.Equal(ExecutionState.NotStarted, plan.Execution);
            Assert.Equal(text == original ? 0 : 1, plan.Plan.Changes.Count);
            Assert.Equal(text, PropertyValueComparison.TextIn(guard.After, "zh_CN"));
            Assert.Equal(guard.Before.Where(t => t.Language != "zh_CN").Select(t => (t.Language, t.Text)),
                guard.After.Where(t => t.Language != "zh_CN").Select(t => (t.Language, t.Text)));
            Assert.All(plan.Guards.Where(g => !g.Main), auxiliary =>
                Assert.Equal(auxiliary.Before.Select(t => (t.Language, t.Text)), auxiliary.After.Select(t => (t.Language, t.Text))));
            TestContext.Current.TestOutputHelper?.WriteLine($"B1 ID 20011, index none, language zh_CN, {related.Length} guarded functions, "
                + $"{plan.Plan.Changes.Count} planned assignments, fingerprint {plan.Plan.Fingerprint}.");
        }

        foreach (SnapshotObject obj in related)
        {
            PropertyReadRow readBack = await ReadAsync(bridge, project, obj, 20011);
            Assert.Equal(adapter.Values[(obj.Type, obj.Oid)].Properties[20011].Texts.OrderBy(t => t.Key, StringComparer.Ordinal)
                .Select(t => (t.Key, t.Value)), (readBack.Texts ?? []).Select(t => (t.Language, t.Text)));
        }

        LiveSnapshots.Snapshot after = await LiveSnapshots.TakeAsync(bridge, new ProjectSnapshotArgs { Project = project });
        Assert.Equal(sample.Snapshot.Hash, after.Hash);
        Assert.Equal(files.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(project).OrderBy(p => p.Key, StringComparer.Ordinal));
        adapter.AssertDisposed();
    }

    [Fact]
    public async Task B1_LargeWrite_StaleLocatorAndUnreviewedProperty_AreRefusedWithoutWriting()
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        await LiveProperties.DescribeEnvironmentAsync(bridge, eplan.ProcessId);
        string project = await LargeWriteAsync(bridge);
        Dictionary<string, string> files = LiveSnapshots.DesignFiles(project);
        LivePlanSample sample = await LivePlanSample.ReadAsync(bridge, project);
        SnapshotObject main = await TextMainAsync(bridge, project, sample, auxiliaryScope: false);
        SnapshotObject[] related = [main];
        var adapter = await CaptureAsync(bridge, project, related);
        string[] languages = await LanguagesAsync(bridge, project);
        PropertySetChange stale = Change(main, "中文");
        stale.Target = new PropertyTarget(locator: Locator(project, main));
        stale.Target.Locator!.Name += "-stale";
        BridgeErrorException staleError = Assert.Throws<BridgeErrorException>(() => new PropertySetPlanner(adapter).Plan(
            project, 2026, [stale], related, languages, TestContext.Current.CancellationToken));
        Assert.Equal(ErrorReason.PlanStale, staleError.Error.Reason);
        Assert.Equal(ExecutionState.NotStarted, staleError.Error.Execution);
        AssertPointer(staleError, "/changes/0/target");

        SnapshotObject rated = LivePropertyObjects.Device(sample.Snapshot, 20027);
        PropertyReadRow technical = await ReadAsync(bridge, project, rated, 20027);
        Assert.Equal("opaque", technical.ValueType); // A44 is not narrowed by planning.
        Assert.Null(technical.Value);
        PropertySetChange unknown = Change(rated, "unreviewed property must stay refused");
        unknown.Property = new PropertySelector(propertyId: 20024);
        int opens = adapter.Opens;
        BridgeErrorException opaqueError = Assert.Throws<BridgeErrorException>(() => new PropertySetPlanner(adapter).Plan(
            project, 2026, [unknown], [rated], languages, TestContext.Current.CancellationToken));
        Assert.Equal(ErrorReason.PolicyDenied, opaqueError.Error.Reason);
        AssertPointer(opaqueError, "/changes/0/property");
        Assert.Equal(opens, adapter.Opens);
        Assert.Equal([(2026, "Function", 20011, ""), (2026, "Function", 20027, "")], PropertyWriteWhitelist.Entries.Keys.OrderBy(k => k.PropertyId));
        Assert.Equal(files.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(project).OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    private static async Task<string> LargeWriteAsync(BridgeClient bridge)
    {
        ProjectListData open = await bridge.InvokeAsync<ProjectListArgs, ProjectListData>(OperationNames.ProjectList,
            new ProjectListArgs(), TestContext.Current.CancellationToken);
        string wanted = Path.GetFullPath(Path.Combine(WriteHost.ScratchFolder, WriteHost.WritableName));
        OpenProject project = Assert.Single(open.Projects, p => string.Equals(Path.GetFullPath(p.Path), wanted, StringComparison.OrdinalIgnoreCase));
        TestContext.Current.TestOutputHelper?.WriteLine("P6 uses the existing writes/LargeWrite fixture; no copy is created or modified.");
        return project.Path;
    }

    private static async Task<SnapshotObject> TextMainAsync(BridgeClient bridge, string project, LivePlanSample sample, bool auxiliaryScope)
    {
        SnapshotObject[] functions = sample.Snapshot.Objects("function").ToArray();
        SnapshotObject[] candidates = functions
            .Where(f => f.Main == true && f.Name is not null && f.Values.Any(v => v.Id == 20011))
            .Where(f => sample.Functions.Count(other => other.Name == f.Name && other.IsMain) == 1
                && (!auxiliaryScope || sample.Functions.Any(other => other.Name == f.Name && !other.IsMain)
                    && sample.Functions.Where(other => other.Name == f.Name).All(other => other.Page is not null)))
            .OrderBy(f => f.Name, StringComparer.Ordinal).ThenBy(f => f.Page, StringComparer.Ordinal)
            .ThenBy(f => f.Type).ThenBy(f => f.Oid).ToArray();
        var refusals = new List<string>();
        int minimumLanguages = auxiliaryScope ? 1 : 2;
        foreach (SnapshotObject candidate in candidates)
        {
            PropertyReadRow main = await ReadAsync(bridge, project, candidate, 20011);
            if (main.Status != "value" || main.Texts?.Count < minimumLanguages
                || main.Texts is null)
            {
                Refused(candidate, main);
                continue;
            }

            if (!auxiliaryScope)
            {
                return candidate;
            }

            bool readable = true;
            foreach (SnapshotObject related in functions.Where(f => f.Name == candidate.Name
                && (f.Type != candidate.Type || f.Oid != candidate.Oid)).OrderBy(f => f.Type).ThenBy(f => f.Oid))
            {
                PropertyReadRow row = await ReadAsync(bridge, project, related, 20011);
                if (row.Status is not ("value" or "empty"))
                {
                    Refused(related, row);
                    readable = false;
                    break;
                }
            }

            if (readable)
            {
                return candidate;
            }
        }

        string requirement = auxiliaryScope ? "a same-name auxiliary group with readable FUNC_TEXT" : "at least two stored languages";
        // On the sample fixture every device with same-name auxiliaries reads 20011 as read_refused/unreviewed (2026-10-26):
        // its runtime object type is not reviewed in the catalog. The planner refuses such targets (Small test); skip here.
        Assert.SkipWhen(auxiliaryScope && refusals.Count > 0 && refusals.All(r => r.Contains(": read_refused unreviewed;", StringComparison.Ordinal)),
            "No reviewed auxiliary sample: " + string.Join("; ", refusals));
        throw new InvalidOperationException($"No B1 Function FUNC_TEXT (20011) sample among {candidates.Length} candidates reads as value "
            + "through property.read with " + requirement + ". Rejected targets: " + string.Join("; ", refusals));

        void Refused(SnapshotObject obj, PropertyReadRow row)
        {
            string reason = $"{obj.Name} {obj.Type}/{obj.Oid}: {row.Status} {row.Reason}; "
                + $"stored_languages={row.Texts?.Count ?? 0}; requires Function, value and {minimumLanguages} stored languages on the main";
            refusals.Add(reason);
            TestContext.Current.TestOutputHelper?.WriteLine("Skipped B1 candidate: " + reason);
        }
    }

    private static async Task<CapturedAdapter> CaptureAsync(BridgeClient bridge, string project, SnapshotObject[] functions)
    {
        var adapter = new CapturedAdapter();
        foreach (SnapshotObject obj in functions)
        {
            PropertyReadRow row = await ReadAsync(bridge, project, obj, 20011);
            Assert.True(row.Status is "value" or "empty", $"{obj.Name} {obj.Type}/{obj.Oid}: {row.Status} {row.Reason}");
            Assert.Equal("multi_language", row.Definition!.ValueType);
            List<SnapshotText> snapshotTexts = obj.Values.SingleOrDefault(v => v.Id == 20011)?.Texts ?? [];
            // P6 is unregistered. These fixture DTOs must match the independent normalized snapshot; reject differing
            // views instead of planning from truncated text. Small/Medium tests cover raw controls and long suffixes.
            Assert.Equal(snapshotTexts.OrderBy(t => t.Language, StringComparer.Ordinal).Select(t => (t.Language, t.Text)),
                (row.Texts ?? []).Select(t => (t.Language, t.Text)));
            var captured = new FakePropertyReadAdapter
            {
                ObjectType = "Function",
            };
            var property = new FakeProperty
            {
                Metadata = row.Definition,
                Empty = row.Texts is null,
            };
            foreach (SnapshotText text in row.Texts ?? [])
            {
                property.Texts.Add(text.Language, text.Text);
            }

            captured.Properties.Add(20011, property);
            adapter.Values.Add((obj.Type, obj.Oid), captured);
        }

        return adapter;
    }

    private static async Task<string[]> LanguagesAsync(BridgeClient bridge, string project)
    {
        // This existing plan operation reads settings. Its execute operation is never called.
        WritePlan languagePlan = await bridge.InvokeAsync<LanguageSetArgs, WritePlan>(OperationNames.LanguageSetPlan,
            new LanguageSetArgs
            {
                Project = project,
                Display = ["##_##"],
            }, TestContext.Current.CancellationToken);
        return languagePlan.Details![LanguageSetPlanOperation.TranslationBefore].Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private static async Task<PropertyReadRow> ReadAsync(BridgeClient bridge, string project, SnapshotObject obj, int id)
    {
        PropertyReadRow row = Assert.Single(await LiveProperties.ReadAsync(bridge, new PropertyReadArgs
        {
            ProjectPath = project,
            Target = obj.Main == true ? new PropertyTarget(deviceTag: obj.Name, page: obj.Page)
                : new PropertyTarget(locator: Locator(project, obj)),
            Properties = [new PropertySelector(propertyId: id)],
            Detailed = true,
        }));
        return row;
    }

    private static PropertySetChange Change(SnapshotObject main, string text) => new()
    {
        Target = new PropertyTarget(deviceTag: main.Name, page: main.Page),
        Property = new PropertySelector(propertyId: 20011),
        Value = new PropertyTypedValue("multi_language", text: text),
        Language = "zh_CN",
    };

    private static ObjectLocator Locator(string project, SnapshotObject obj) => new()
    {
        Project = project,
        Kind = obj.Kind,
        Type = obj.Type,
        Oid = obj.Oid,
        Name = obj.Name,
        Page = obj.Page,
        Category = obj.Category,
        Pos = obj.Pos,
    };

    private static void AssertPointer(BridgeErrorException error, string pointer)
    {
        using JsonDocument violations = JsonDocument.Parse(error.Error.Metadata!["violations"]);
        Assert.Contains(violations.RootElement.EnumerateArray(), v => v.GetProperty("path").GetString() == pointer);
    }

    private sealed class CapturedAdapter : IPropertyReadAdapter
    {
        public Dictionary<(int, long), FakePropertyReadAdapter> Values { get; } = [];
        public int Opens => Values.Values.Sum(v => v.Opens);

        public IPropertyReadScope Open(string projectPath, PropertyTarget target) =>
            Values[(target.Locator!.Type, target.Locator.Oid)].Open(projectPath, target);

        public void AssertDisposed()
        {
            foreach (FakePropertyReadAdapter reader in Values.Values)
            {
                reader.AssertDisposed();
            }
        }
    }
}
