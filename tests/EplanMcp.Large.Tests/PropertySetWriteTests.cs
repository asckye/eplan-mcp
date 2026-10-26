using System.Security.Cryptography;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PropertySetWriteTests(LiveEplan eplan)
{
    [Fact]
    public async Task B1_LargeWrite_LiteralWritesReadbackRollbackAndSingleUndo_RestoreValuesAndEarlierHistory()
    {
        await using BridgeClient reader = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        await LiveProperties.DescribeEnvironmentAsync(reader, eplan.ProcessId);
        HandshakeResult product = await reader.SelectAsync(eplan.ProcessId, TestContext.Current.CancellationToken);
        Assert.Contains(OperationNames.PropertySetPlan, product.Operations!);
        Assert.Contains(OperationNames.PropertySetExecute, product.Operations!);
        Assert.Empty(PropertyWriteWhitelist.Entries);
        ProjectListData opened = await reader.InvokeAsync<ProjectListArgs, ProjectListData>(OperationNames.ProjectList, new(), TestContext.Current.CancellationToken);
        string project = await eplan.WriteProjectAsync();
        Assert.Equal(Path.GetFullPath(Path.Combine(WriteHost.ScratchFolder, WriteHost.WritableName)), Path.GetFullPath(project), ignoreCase: true);
        Dictionary<string, string> others = OtherDesignFiles(opened, project);
        try
        {
            // A41/A46: this existing helper selects LargeWrite by closing only known fixture copies,
            // checks the current project immediately before each remote undo, and restores the original selection.
            await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
            await using PropertyWriteBridge writer = await PropertyWriteBridge.StartAsync(eplan.ProcessId);
            LiveSnapshots.Snapshot snapshot = await selection.SnapshotAsync();
            PropertySetArgs baseline = await CandidatesAsync(writer, reader, project, snapshot);
            List<PropertySetChange> restore = RestoreChanges(baseline);
            string[] sentinel = await selection.CreatePagesAsync(1);
            string state = await selection.StateAsync();
            try
            {
                foreach (string literal in new[] { "14 W", "DC 24V", "I/O Board", "", " 中文;de_DE; @literal; @ " })
                {
                    baseline.Changes[1].Value = new PropertyTypedValue("text", text: literal);
                    PropertySetArgs request = await writer.PlanAsync(project, baseline.Changes);
                    Assert.Equal(2, request.Approved.Plan.Changes.Count);

                    PropertySetData committed = await writer.ExecuteAsync(request);
                    Assert.Equal(ExecutionState.Committed, committed.Execution);
                    Assert.Equal("AI: eplan_property_set", committed.Undo);
                    Assert.Equal(2, committed.Plan.Changes.Count);
                    await ReadbackAsync(reader, project, request.Approved.Guards, after: true);

                    PropertySetArgs fault = await writer.PlanAsync(project,
                    [
                        Change(baseline.Changes[0].Target.Locator!, 20011, "rollback; @ 中文", "zh_CN"),
                        Change(baseline.Changes[1].Target.Locator!, 20027, "rollback I/O Board"),
                    ]);
                    // Probe an embedded NUL in the last changing item, after a valid native write.
                    // A setter rejection or a lost suffix must roll back the preceding assignment as well.
                    PropertySetGuard last = fault.Approved.Guards.Last(g => !g.Before.Select(t => (t.Language, t.Text))
                        .SequenceEqual(g.After.Select(t => (t.Language, t.Text))));
                    PropertySetChange rejected = fault.Changes.Single(c => c.Target.Locator!.Type == last.Target.Type
                        && c.Target.Locator.Oid == last.Target.Oid && c.Property.PropertyId == last.PropertyId);
                    rejected.Value = new PropertyTypedValue(rejected.Value.ValueType, text: rejected.Value.Text + "\0suffix");
                    fault = await writer.PlanAsync(project, fault.Changes);
                    Assert.Equal(2, fault.Approved.Plan.Changes.Count);
                    BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => writer.ExecuteAsync(fault));
                    Assert.Equal(ExecutionState.RolledBack, error.Error.Execution);
                    TestContext.Current.TestOutputHelper!.WriteLine("Native B1 rollback reason: " + error.Error.Reason);
                    await ReadbackAsync(reader, project, request.Approved.Guards, after: true);

                    // The rolled-back call adds no empty undo step: one undo must remove the committed B1 call.
                    await selection.UndoAsync();
                    await ReadbackAsync(reader, project, request.Approved.Guards, after: false);
                    Assert.Equal(state, await selection.StateAsync());
                    Assert.Contains((await selection.SnapshotAsync()).Pages, p => p.Name == sentinel[0]);
                    TestContext.Current.TestOutputHelper!.WriteLine($"B1 20011 zh_CN + 20027 independent ({literal.Length} chars): committed, independent readback, native SafetyPoint rollback, one current-project XEsUndoAction restored both values.");
                }

                // Earlier history is still usable, after every successful/failed B1 call.
                await selection.UndoAsync();
                Assert.DoesNotContain((await selection.SnapshotAsync()).Pages, p => p.Name == sentinel[0]);
            }
            finally
            {
                PropertySetArgs cleanup = await writer.PlanAsync(project, restore);
                PropertySetData restored = await writer.ExecuteAsync(cleanup);
                Assert.True(restored.Execution is ExecutionState.NotStarted or ExecutionState.Committed);
                await ReadbackAsync(reader, project, baseline.Approved.Guards, after: false);
            }
        }
        finally
        {
            Assert.Equal(others.OrderBy(p => p.Key, StringComparer.Ordinal), OtherDesignFiles(opened, project).OrderBy(p => p.Key, StringComparer.Ordinal));
        }
    }

    [Fact]
    public async Task B1_LargeWrite_StaleGuard_IsNotStartedAndDesignFilesStayUnchanged()
    {
        await using BridgeClient reader = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        string project = await eplan.WriteProjectAsync();
        await using PropertyWriteBridge writer = await PropertyWriteBridge.StartAsync(eplan.ProcessId);
        LiveSnapshots.Snapshot snapshot = await LiveSnapshots.TakeAsync(reader, new ProjectSnapshotArgs { Project = project });
        PropertySetArgs args = await CandidatesAsync(writer, reader, project, snapshot);
        Dictionary<string, string> before = LiveSnapshots.DesignFiles(project);
        args.Approved.Guards[0].Parts = ["stale guard"];
        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => writer.ExecuteAsync(args));
        Assert.Equal(ErrorReason.PlanStale, error.Error.Reason);
        Assert.Equal(ExecutionState.NotStarted, error.Error.Execution);
        Assert.Equal(before.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(project).OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    private static async Task<PropertySetArgs> CandidatesAsync(PropertyWriteBridge writer, BridgeClient reader, string project, LiveSnapshots.Snapshot snapshot)
    {
        var chosen = new List<PropertySetChange>();
        foreach (int id in new[] { 20011, 20027 })
        {
            foreach (SnapshotObject main in snapshot.Objects("function").Where(f => f.Main == true && f.Values.Any(v => v.Id == id))
                .OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                ObjectLocator locator = Locator(project, main);
                PropertyReadRow read = Assert.Single(await LiveProperties.ReadAsync(reader, ReadArgs(project, locator, id)));
                if (read.Status != "value" || id == 20011 && (read.Texts is null || read.Texts.Count < 2)
                    || id == 20027 && new[] { "14 W", "DC 24V", "I/O Board", "", " 中文;de_DE; @literal; @ " }.Contains(read.Display, StringComparer.Ordinal))
                {
                    continue;
                }

                PropertySetChange change = Change(locator, id, id == 20011 ? "P7 输送电机; @" : "DC 24V; I/O Board", id == 20011 ? "zh_CN" : null);
                try
                {
                    PropertySetArgs planned = await writer.PlanAsync(project, [change]);
                    if (planned.Approved.Plan.Changes.Count != 1)
                    {
                        continue;
                    }

                    chosen.Add(change);
                    break;
                }
                catch (BridgeCallException ex) when (ex.Error.Reason is ErrorReason.PolicyDenied or ErrorReason.CapabilityMissing)
                {
                    TestContext.Current.TestOutputHelper!.WriteLine($"B1 candidate {main.Name}, {id}: {ex.Error.Reason}.");
                }
            }
        }

        Assert.Equal(2, chosen.Count);
        return await writer.PlanAsync(project, chosen);
    }

    private static async Task ReadbackAsync(BridgeClient reader, string project, List<PropertySetGuard> guards, bool after)
    {
        foreach (PropertySetGuard guard in guards)
        {
            PropertyReadRow actual = Assert.Single(await LiveProperties.ReadAsync(reader, ReadArgs(project, guard.Target, guard.PropertyId)));
            List<SnapshotText> expected = after ? guard.After : guard.Before;
            Assert.True(actual.Status is "value" or "empty", $"{guard.PropertyId}: {actual.Status} {actual.Reason} {actual.ValueType} [{actual.Display}]");
            if (guard.PropertyId == 20027)
            {
                Assert.Equal(PropertyValueComparison.TextIn(expected, TextLanguages.Independent), actual.Display ?? "");
                // An empty 20027 reads back as "empty" without a value type.
                Assert.Equal(actual.Status == "value" ? "opaque" : null, actual.ValueType);
            }
            else
            {
                Assert.Equal(expected.Where(t => t.Text.Length > 0).OrderBy(t => t.Language, StringComparer.Ordinal).Select(t => (t.Language, t.Text)),
                    (actual.Texts ?? []).Where(t => t.Text.Length > 0).OrderBy(t => t.Language, StringComparer.Ordinal).Select(t => (t.Language, t.Text)));
            }
        }
    }

    private static List<PropertySetChange> RestoreChanges(PropertySetArgs baseline) => baseline.Changes.Select(change =>
    {
        PropertySetGuard guard = baseline.Approved.Guards.Single(g => g.Target.Type == change.Target.Locator!.Type
            && g.Target.Oid == change.Target.Locator.Oid && g.PropertyId == change.Property.PropertyId);
        return Change(guard.Target, guard.PropertyId, PropertyValueComparison.TextIn(guard.Before, change.Language ?? TextLanguages.Independent), change.Language);
    }).ToList();

    private static PropertySetChange Change(ObjectLocator target, int id, string text, string? language = null) => new()
    {
        Target = new PropertyTarget(locator: target),
        Property = new PropertySelector(propertyId: id),
        Language = language,
        Value = new PropertyTypedValue(id == 20011 ? "multi_language" : "text", text: text),
    };

    private static ObjectLocator Locator(string project, SnapshotObject obj) => new()
    {
        Project = project,
        Kind = "function",
        Type = obj.Type,
        Oid = obj.Oid,
        Name = obj.Name,
        Page = obj.Page,
        Category = obj.Category,
        Pos = obj.Pos,
    };

    private static PropertyReadArgs ReadArgs(string project, ObjectLocator locator, int id) => new()
    {
        ProjectPath = project,
        Target = new PropertyTarget(locator: locator),
        Properties = [new PropertySelector(propertyId: id)],
        Detailed = true,
    };

    private static Dictionary<string, string> OtherDesignFiles(ProjectListData opened, string project) => opened.Projects
        .Where(p => !string.Equals(p.Path, project, StringComparison.OrdinalIgnoreCase))
        .SelectMany(p => LiveSnapshots.DesignFiles(p.Path).Where(f => !string.Equals(f.Key, "ProjectInfo.xml", StringComparison.OrdinalIgnoreCase))
            .Select(f => new KeyValuePair<string, string>(p.Path + "/" + f.Key, f.Value))
            .Append(new KeyValuePair<string, string>(p.Path + ".elk", LinkHash(p.Path + ".elk"))))
        .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    private static string LinkHash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
