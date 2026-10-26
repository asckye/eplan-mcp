using System.Diagnostics;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Newtonsoft.Json.Linq;
using Action = System.Action;

namespace EplanMcp.Spikes.S38;

public sealed partial class SpikeS38Action
{
    private static ISOCode.Language Language(string code)
    {
        if (code == "??_??") { return ISOCode.Language.L___; }
        using var iso = new ISOCode(code);
        return iso.GetNumber();
    }

    private static MultiLangString MultiText(JObject translations)
    {
        var text = new MultiLangString();
        try
        {
            foreach (JProperty entry in translations.Properties()) { text.AddString(Language(entry.Name), entry.Value.Value<string>()!); }
            return text;
        }
        catch { text.Dispose(); throw; }
    }

    private static JObject Changed(JObject translations)
    {
        var changed = (JObject)translations.DeepClone();
        JProperty first = changed.Properties().First();
        first.Value = first.Value.Value<string>() + " [S38 probe]";
        return changed;
    }

    private static void WriteText(Text text, JObject translations)
    {
        using MultiLangString value = MultiText(translations);
        text.Contents = value;
    }

    private static void SetProperty(UniversalPropertyList list, int number, int index, JObject expected)
    {
        using AnyPropertyId id = number;
        using var value = new PropertyValue();
        if (expected["languages"] is JObject translations)
        {
            using MultiLangString text = MultiText(translations);
            value.Set(text);
        }
        else { value.Set(expected.Value<string>("display")!); }
        if (index > 0) { list[id, index] = value; }
        else { list[id] = value; }
    }

    private static JObject ChangedProperty(JObject before)
    {
        var expected = (JObject)before.DeepClone();
        expected["empty"] = false;
        if (before.Value<string>("type") == "MultilangString")
        {
            expected["languages"] = before["languages"] is JObject languages && languages.Count > 0 ? Changed(languages) : new JObject { ["en_US"] = "S38 probe" };
        }
        else { expected["display"] = (before.Value<string>("display") ?? string.Empty) + " [S38 probe]"; }
        return expected;
    }

    private static bool Rollback(JObject owner, string name, string project, Func<JToken> read, Action write, Func<JToken, bool> matches)
    {
        var watch = Stopwatch.StartNew();
        var row = new JObject();
        owner[name] = row;
        JToken before = read();
        row["before"] = before.DeepClone();
        JToken filesBefore = Evidence.Capture(row, "hashesBefore", () => Evidence.Hashes(project));
        if (filesBefore["files"] is not JObject) { throw new InvalidOperationException("Design-file hashing failed before write."); }
        Publish(owner["request"]!.Value<string>("checkpoint")!, owner);
        try
        {
            using SafetyPoint safety = SafetyPoint.Create();
            using var manager = new UndoManager();
            UndoStep undo = manager.CreateUndoStep(); // Owned by UndoManager, as in the Bridge TransactionScope.
            undo.SetUndoDescription("S38: " + name);
            try
            {
                using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
                var writeWatch = Stopwatch.StartNew();
                try { write(); }
                finally { row["writeMs"] = writeWatch.Elapsed.TotalMilliseconds; }
                JToken after = read();
                row["afterWrite"] = after.DeepClone();
                row["changed"] = !JToken.DeepEquals(before, after);
                row["expectedReadback"] = matches(after);
                Evidence.Capture(row, "hashesAfterWrite", () => Evidence.Hashes(project));
            }
            catch (Exception ex) { row["writeError"] = Evidence.Error(ex); }
            finally
            {
                var rollbackWatch = Stopwatch.StartNew();
                try { safety.Rollback(); row["rollbackReturned"] = true; }
                finally { row["rollbackMs"] = rollbackWatch.Elapsed.TotalMilliseconds; }
            }
        }
        catch (Exception ex) { row["scopeError"] = Evidence.Error(ex); }
        finally
        {
            JToken after = Evidence.Capture(row, "afterRollback", read);
            row["restored"] = JToken.DeepEquals(before, after);
            JToken filesAfter = Evidence.Capture(row, "hashesAfterRollback", () => Evidence.Hashes(project));
            Evidence.Capture(row, "designFileComparison", () => Evidence.CompareHashes(filesBefore, filesAfter));
            row["hashEvidenceComplete"] = filesAfter["files"] is JObject;
            row["elapsedMs"] = watch.Elapsed.TotalMilliseconds;
        }
        if (row.Value<bool?>("restored") != true || row["scopeError"] is not null)
        {
            throw new InvalidOperationException("Rollback could not be verified; stop and retain this scratch copy for inspection.");
        }
        return row.Value<bool?>("changed") == true && row.Value<bool?>("expectedReadback") == true && row.Value<bool?>("hashEvidenceComplete") == true && row["writeError"] is null;
    }

    private static void Replace(Project project, Page page, PropertyLists lists, JObject report, JObject request, Action outputCheckpoint)
    {
        RequireUndoTarget(project);
        report["inventory"] = Inventory(page, lists);
        var traversal = new JObject();
        Text[] texts = Placements(page, traversal).Where(p => p.Origin == "page" && p.Object.GetType() == typeof(Text)).Select(p => (Text)p.Object).OrderBy(t => t.ObjectIdentifier).ToArray();
        report["candidateTraversal"] = traversal;
        if (((JArray)traversal["traversalErrors"]!).Count > 0) { throw new InvalidOperationException("Text traversal is incomplete; refusing writes."); }
        var candidates = texts.Select(t => (Text: t, Content: Content(t))).ToArray();
        long? freeId = request.Value<long?>("freeId");
        long? multiId = request.Value<long?>("multiId");
        Text? free = candidates.FirstOrDefault(p => p.Content.Count == 1 && (freeId is null || p.Text.ObjectIdentifier == freeId)).Text;
        Text? multi = candidates.FirstOrDefault(p => p.Content.Count >= 2 && (multiId is null || p.Text.ObjectIdentifier == multiId)).Text;
        if (free is null || multi is null)
        {
            report["incomplete"] = "Need one exact Graphics.Text with one stored language and another with at least two stored languages on this page; use texts mode to choose --free-id/--multi-id. No objects were created or changed.";
            return;
        }
        JObject beforeFree = Content(free);
        JObject beforeMulti = Content(multi);
        JObject expectedFree = Changed(beforeFree);
        JObject expectedMulti = Changed(beforeMulti);
        report["targets"] = new JObject { ["free"] = Identity(free), ["multi"] = Identity(multi), ["expectedFree"] = expectedFree, ["expectedMulti"] = expectedMulti };
        outputCheckpoint();
        bool rolledBack = Rollback(report, "replaceRollback", project.ProjectLinkFilePath,
            () => new JObject { ["free"] = Content(free), ["multi"] = Content(multi) },
            () => { WriteText(free, expectedFree); WriteText(multi, expectedMulti); },
            after => JToken.DeepEquals(after["free"], expectedFree) && JToken.DeepEquals(after["multi"], expectedMulti));
        if (!rolledBack) { report["incomplete"] = "Replacement did not pass readback; no commit or remote undo attempted."; return; }

        RequireUndoTarget(project);
        var commit = new JObject { ["target"] = Identity(free), ["before"] = beforeFree, ["expected"] = expectedFree, ["committed"] = false };
        report["commit"] = commit;
        commit["hashesBefore"] = Evidence.Hashes(project.ProjectLinkFilePath);
        outputCheckpoint();
        var watch = Stopwatch.StartNew();
        using (SafetyPoint safety = SafetyPoint.Create())
        using (var manager = new UndoManager())
        {
            UndoStep undo = manager.CreateUndoStep();
            undo.SetUndoDescription("S38: replace one page text");
            using (var quiet = new QuietModeStep(QuietModes.ShowNoDialogs))
            {
                WriteText(free, expectedFree);
                commit["readback"] = Content(free);
                if (!JToken.DeepEquals(commit["readback"], expectedFree)) { throw new InvalidOperationException("Commit readback failed; SafetyPoint will roll back."); }
            }
            safety.Commit();
            commit["committed"] = true;
            undo.CloseUndo();
        }
        commit["elapsedMs"] = watch.Elapsed.TotalMilliseconds;
        commit["after"] = Content(free);
        commit["hashesAfter"] = Evidence.Hashes(project.ProjectLinkFilePath);
        commit["designFileComparison"] = Evidence.CompareHashes(commit["hashesBefore"]!, commit["hashesAfter"]!);
        outputCheckpoint();
    }

    private static Text CommittedText(Page page, JObject request)
    {
        JObject baseline = (JObject)request["baseline"]!;
        if (!JToken.DeepEquals(baseline["page"], new JObject(Identity(page)) { ["name"] = page.Name }) ||
            !string.Equals(baseline.Value<string>("project"), page.Project.ProjectLinkFilePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Project/page identity changed since commit; refusing undo verification.");
        }
        JObject identity = (JObject)baseline["commit"]!["target"]!;
        Text? text = Placements(page, new JObject()).Select(p => p.Object).OfType<Text>().SingleOrDefault(t => t.ObjectIdentifier == identity.Value<long>("oid") && t.GetType() == typeof(Text));
        if (text is null || !JToken.DeepEquals(Identity(text), identity)) { throw new InvalidOperationException("Committed text identity is stale."); }
        return text;
    }

    private static void VerifyCommitted(Page page, JObject request, JObject report)
    {
        Text text = CommittedText(page, request);
        report["content"] = Content(text);
        if (!JToken.DeepEquals(report["content"], request["baseline"]!["commit"]!["after"]))
        {
            throw new InvalidOperationException("Text changed after commit; do not execute XEsUndoAction.");
        }
        JObject comparison = Evidence.CompareHashes(request["baseline"]!["hashesAfter"]!, report["hashesBefore"]!);
        report["unchangedSinceCommit"] = comparison;
        if (comparison.Value<bool>("equal") != true) { throw new InvalidOperationException("Design files changed since commit; refusing XEsUndoAction."); }
    }

    private static void VerifyUndo(Page page, JObject request, JObject report)
    {
        RequireUndoTarget(page.Project);
        Text text = CommittedText(page, request);
        report["content"] = Content(text);
        report["oneUndoRestored"] = JToken.DeepEquals(report["content"], request["baseline"]!["commit"]!["before"]);
        JToken files = Evidence.Hashes(page.Project.ProjectLinkFilePath);
        report["hashesAfterUndo"] = files;
        report["undoVsBeforeCommit"] = Evidence.CompareHashes(request["baseline"]!["commit"]!["hashesBefore"]!, files);
        report["undoVsModeBefore"] = Evidence.CompareHashes(request["baseline"]!["hashesBefore"]!, files);
    }

    private static JObject FrameState(Page page, PropertyLists lists) => new()
    {
        ["setting11016"] = ReadProperty(lists.Get(page), 11016, 0), ["effectiveFrame"] = page.PlotFrame?.Name,
    };

    private static JArray FrameRelated(UniversalPropertyList list, Type propertyEnum)
    {
        var rows = new JArray();
        AnyPropertyId[] ids = list.ExistingIds;
        try
        {
            foreach (AnyPropertyId id in ids)
            {
                string name = Enum.GetName(propertyEnum, id.AsInt) ?? string.Empty;
                if (name.IndexOf("FRAME", StringComparison.Ordinal) < 0 && name.IndexOf("FORMPLOT", StringComparison.Ordinal) < 0) { continue; }
                var row = new JObject { ["id"] = id.AsInt, ["index"] = id.Index, ["name"] = name };
                Evidence.Capture(row, "value", () => ReadProperty(list, id.AsInt, id.Index));
                rows.Add(row);
            }
        }
        finally { foreach (AnyPropertyId id in ids) { id.Dispose(); } }
        return rows;
    }

    private static JObject DisplayedState(Page page, UniversalPropertyList list, int number, int index)
    {
        var evidence = new JObject();
        var texts = new JArray();
        foreach ((Placement item, string origin) in Placements(page, evidence))
        {
            if (origin != "plotFrame" || item is not TextBase text) { continue; }
            if (item is not (PropertyPlacement or PlaceHolderText)) { continue; }
            JObject binding = Binding(text, page, origin);
            if (binding.Value<int?>("propertyId") != number || binding.Value<int?>("index") != index) { continue; }
            var row = new JObject { ["placement"] = Identity(item) };
            Evidence.Capture(row, "content", () => Content(text));
            if (item is PropertyPlacement p2) { Evidence.Capture(row, "displayString", () => new JValue(p2.GetDisplayString())); }
            texts.Add(row);
        }
        // Timings are diagnostics, not part of the state compared before/after rollback.
        foreach (JProperty timing in texts.Descendants().OfType<JProperty>().Where(p => p.Name == "timingsMs").ToArray()) { timing.Remove(); }
        return new JObject { ["value"] = ReadProperty(list, number, index), ["frameTexts"] = texts, ["traversalErrors"] = evidence["traversalErrors"]!.DeepClone() };
    }

    private static void TitleBlock(Project project, Page page, PropertyLists lists, JObject report, JObject request)
    {
        report["frameBefore"] = FrameState(page, lists);
        Evidence.Capture(report, "frameRelatedPageProperties", () => FrameRelated(lists.Get(page), typeof(Properties.Page)));
        Evidence.Capture(report, "frameRelatedProjectProperties", () => FrameRelated(lists.Get(project), typeof(Properties.Project)));
        JObject inventory = Inventory(page, lists);
        report["inventory"] = inventory;
        var bindings = new JArray();
        report["displayedProperties"] = bindings;
        foreach (JObject row in ((JArray)inventory["objects"]!).OfType<JObject>().Where(r => r.Value<string>("origin") == "plotFrame" && r["binding"] is JObject))
        {
            var binding = (JObject)row["binding"]!.DeepClone();
            binding["placement"] = row["sessionIdentifier"]!.DeepClone();
            bindings.Add(binding);
            string? scope = binding.Value<string>("scope");
            if (scope is "page" or "project")
            {
                Evidence.Capture(binding, "value", () => ReadProperty(lists.Get(scope == "page" ? page : project), binding.Value<int>("propertyId"), binding.Value<int>("index")));
            }
            DiscoveryEvidence.ClassifyCandidate(binding);
        }
        report["propertyDiscovery"] = new JObject
        {
            ["frameBindings"] = bindings.Count,
            ["readableDescriptors"] = bindings.Count(b => b.Value<bool?>("descriptorComplete") == true),
            ["inferredScopes"] = bindings.Count(b => b.Value<bool?>("inferred") == true),
            ["eligiblePageProperties"] = bindings.Count(b => b.Value<string>("scope") == "page" && b.Value<bool?>("eligible") == true),
            ["eligibleProjectProperties"] = bindings.Count(b => b.Value<string>("scope") == "project" && b.Value<bool?>("eligible") == true),
        };
        report["reachability"] = "Displayed descriptors, native sources and relation getters are recorded independently. Contextual frame sources may be unavailable; a displayed PAGE_/PROJ_ catalog name can infer the target scope, explicitly marked inferred. An unreadable descriptor never becomes an invented binding. Formula/indirection/rendered visibility cannot be established; no visual verification is performed.";
        foreach (string scope in new[] { "page", "project" })
        {
            JObject? binding = bindings.OfType<JObject>().Where(b => b.Value<string>("scope") == scope && b.Value<bool?>("eligible") == true)
                .OrderBy(b => b.Value<bool>("inferred")).ThenBy(b => b.Value<int>("propertyId")).ThenBy(b => b.Value<int>("index")).FirstOrDefault();
            if (binding is null)
            {
                report[scope + "PropertyProbe"] = new JObject { ["skipped"] = "No eligible displayed property; see propertyDiscovery and each binding's candidateRejectionReasons (unresolved descriptor/scope, unreadable value, read-only, type or index)." };
                continue;
            }
            int number = binding.Value<int>("propertyId");
            int index = binding.Value<int>("index");
            UniversalPropertyList list = lists.Get(scope == "page" ? page : project);
            JObject expected = ChangedProperty(ReadProperty(list, number, index));
            report[scope + "PropertyTarget"] = binding.DeepClone();
            Rollback(report, scope + "PropertyProbe", project.ProjectLinkFilePath,
                () => DisplayedState(page, list, number, index), () => SetProperty(list, number, index, expected), after => JToken.DeepEquals(after["value"], expected));
        }
        var frameNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (Page candidate in project.Pages)
        {
            if (candidate.PlotFrame is { } frame) { frameNames.Add(frame.Name); }
        }
        string? current = page.PlotFrame?.Name;
        report["existingProjectFrames"] = new JArray(frameNames.OrderBy(n => n, StringComparer.Ordinal));
        string? target = request.Value<string>("frame") ?? frameNames.OrderBy(n => n, StringComparer.Ordinal).FirstOrDefault(n => n != current);
        if (target is null || target == current || !frameNames.Contains(target))
        {
            report["frameProbe"] = new JObject { ["skipped"] = "Need a different frame already used by another page in this scratch project; --frame must be one of existingProjectFrames. No master data is imported." };
            return;
        }
        report["frameTarget"] = target;
        JObject expectedFrame = ReadProperty(lists.Get(page), 11016, 0);
        expectedFrame["empty"] = false;
        expectedFrame["display"] = target;
        Rollback(report, "frameProbe", project.ProjectLinkFilePath, () => FrameState(page, lists),
            () => SetProperty(lists.Get(page), 11016, 0, expectedFrame),
            after => after.Value<string>("effectiveFrame") == target && JToken.DeepEquals(after["setting11016"], expectedFrame));
    }
}
