using System.Globalization;
using Eplan.EplApi.ApplicationFramework;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;
using Newtonsoft.Json;

namespace EplanMcp.Large.DrawingHelper;

/// <summary>Native rollback probe on the helper's private copy; only detached DTOs are accessed after rollback (A55).</summary>
internal static class TextCreation
{
    public static string Run(string project, string sourcePage)
    {
        var catalog = new EplanProjectCatalog();
        var pages = new EplanPageReader();
        var writer = new EplanPageWriter();
        var transactions = new EplanTransactions();
        string original = Snapshot(project);
        string structure = PageNames.StructureOf(sourcePage);
        string sentinel = structure + "/" + (pages.PageNames(project).Where(n => PageNames.StructureOf(n) == structure)
            .Select(n => int.Parse(n.Substring(n.LastIndexOf('/') + 1), CultureInfo.InvariantCulture)).Max() + 100).ToString(CultureInfo.InvariantCulture);
        var create = new PageCreateArgs { Project = project, Pages = [new() { Name = sentinel, PageType = "Circuit" }] };
        create.Fingerprint = ((WritePlan)new PageCreatePlanOperation(catalog, pages, writer).Execute(create)!).Fingerprint;
        new PageCreateExecuteOperation(catalog, pages, writer, transactions).Execute(create);
        string before = State(project, sentinel);
        string display = LanguageCodes.Split(new EplanProjectLanguages().Read(project).Display).FirstOrDefault(LanguageCodes.IsCode) ?? "??_??"; // unset display: language-independent text (A54)
        var args = new PlacementTextCreateArgs
        {
            Project = project,
            Page = sentinel,
            Text = new() { Position = [50, 50], HeightMm = 3.5, Contents = display == "??_??" ? new() { [display] = "Visible panel" } : new() { [display] = "Visible panel", [display == "en_US" ? "de_DE" : "en_US"] = "Other language" } },
        };
        BridgeError error;
        try
        {
            new PlacementTextCreateExecuteOperation(catalog, new MismatchedWriter(), transactions).Execute(args);
            throw new InvalidOperationException("A forced mismatch unexpectedly committed.");
        }
        catch (BridgeErrorException ex) when (ex.Error.Reason == ErrorReason.EplanOperationFailed && ex.Error.Execution == ExecutionState.RolledBack)
        { error = ex.Error; }
        string after = State(project, sentinel);
        if (after != before)
        { throw new InvalidOperationException("Rollback did not restore the drawing snapshot."); }
        using var context = new ActionCallingContext();
        using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
        if (!(new ActionManager().FindAction("XEsUndoAction") ?? throw new InvalidOperationException("Undo action is unavailable.")).Execute(context))
        { throw new InvalidOperationException("The sentinel undo failed."); }
        // One ordinary next undo must remove the sentinel page; an empty failed undo step would leave it in place.
        bool sentinelRemoved = !pages.PageNames(project).Contains(sentinel, StringComparer.Ordinal);
        string afterUndo = Snapshot(project);
        if (!sentinelRemoved || afterUndo != original)
        { throw new InvalidOperationException("Failed creation left an undo step or changed the original snapshot."); }
        return JsonConvert.SerializeObject(new { original, before, after, afterUndo, sentinelRemoved, execution = error.Execution });
    }

    private static string State(string project, string page) => SnapshotCanonical.Sha256(Snapshot(project) + "\n" +
        JsonConvert.SerializeObject(new EplanDrawingReader().Read(project, page)));

    private static string Snapshot(string project)
    {
        var operation = new ProjectSnapshotOperation(new EplanProjectCatalog(), new EplanSnapshotReader(), new EplanProjectFiles());
        var args = new ProjectSnapshotArgs { Project = project };
        var pages = new List<SnapshotPage>();
        do
        {
            var chunk = (ProjectSnapshotData)operation.Execute(args)!;
            pages.AddRange(chunk.Pages);
            args.AfterPage = chunk.NextAfterPage;
        } while (args.AfterPage is not null);
        return SnapshotCanonical.SnapshotHash(SnapshotProfiles.StandardName, pages);
    }

    private sealed class MismatchedWriter : IPlacementTextWriter
    {
        private readonly EplanPlacementTextWriter _native = new();
        public void Validate(string projectPath, string page, PlacementText text) => _native.Validate(projectPath, page, text);
        public PlacementText Create(string projectPath, string page, PlacementText text)
        {
            PlacementText read = _native.Create(projectPath, page, text);
            read.HeightMm += 1;
            return read;
        }
    }
}
