using System.Collections.Specialized;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Eplan.Adapters;
/// <summary>A9/A39/A57: borrowed objects; SelectionSpikeTests cover the unverified UI behavior, never assumed success.</summary>
internal sealed class EplanSelection : ISelectionAdapter
{
    public SelectionData Read()
    {
        MainThread.Assert();
        var selection = new SelectionSet { LockProjectByDefault = false, LockSelectionByDefault = false };
        var result = new SelectionData { Project = selection.GetCurrentProject(false)?.ProjectFullName, Page = (selection.CurrentlyEdited as Page)?.Name };
        var identities = new List<string>();
        foreach (StorableObject obj in selection.Selection ?? [])
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            using AnyPropertyId? displayed = (obj as PropertyPlacement)?.DisplayedProperty;
            identities.Add(Newtonsoft.Json.JsonConvert.SerializeObject(new { type = obj.GetType().FullName, id = obj.ToStringIdentifier(), property = displayed?.AsInt, position = obj is PropertyPlacement placement ? new[] { placement.Location.X, placement.Location.Y } : null }));
            Project? owner = obj as Project ?? obj.Project;
            ObjectSummary? row = owner is null ? null : Describe(owner.ProjectFullName, obj);
            if (row is null)
            { result.Unsupported++; }
            else
            { result.Objects.Add(row); }
            if (result.Objects.Count + result.Unsupported > 50000)
            { throw new BridgeErrorException(ErrorReason.CapabilityMissing, "Selection exceeds 50000 entries; reduce it in EPLAN."); }
        }
        result.Objects = result.Objects.OrderBy(o => o.Locator.Project, StringComparer.Ordinal).ThenBy(o => o.Locator.Page, StringComparer.Ordinal).ThenBy(o => o.Locator.Type).ThenBy(o => o.Locator.Oid).ToList();
        result.Fingerprint = SnapshotCanonical.Sha256(Newtonsoft.Json.JsonConvert.SerializeObject(identities.OrderBy(i => i, StringComparer.Ordinal)));
        return result;
    }
    private static ObjectSummary? Describe(string project, StorableObject obj)
    {
        if (obj is PropertyPlacement)
        { return null; } // A57: never turn a property selection into its parent.
        string? kind = ObjectReadTypes.Types.FirstOrDefault(t => ObjectReadTypes.Supported(t) && EplanObjectReader.Matches(obj, t.ToLowerInvariant()));
        if (kind is null)
        { return null; }
        SnapshotObject identity = EplanObjectReader.Describe(obj, kind.ToLowerInvariant());
        return new ObjectSummary { Name = SnapshotValues.Text(identity.Name ?? string.Empty), Locator = new DrawingLocator { Project = project, Page = obj is Page p ? p.Name : identity.Page ?? string.Empty, Type = identity.Type, Oid = identity.Oid, Digest = SnapshotCanonical.Sha256(Newtonsoft.Json.JsonConvert.SerializeObject(identity)) } };
    }
    private static List<StorableObject> Resolve(string project, IReadOnlyList<DrawingLocator> locators)
    {
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(project));
        Page? page = finder.GetPages(null).SingleOrDefault(p => p.Name == locators[0].Page);
        if (page is null)
        { throw new BridgeErrorException(ErrorReason.PageNotFound, "Page not found; call eplan_page_list."); }
        Placement[] candidates = page.AllPlacements.Where(p => p is not PropertyPlacement and not Page).ToArray();
        return locators.Select(l => candidates.FirstOrDefault(o => o.ObjectIdentifier == l.Oid && SnapshotCanonical.TypeOf(o.ToStringIdentifier()) == l.Type
            && Describe(project, o) is { } row && DrawingLocators.Matches(l, row.Locator)) as StorableObject
            ?? throw new BridgeErrorException(ErrorReason.PlanStale, "Object is stale or unsupported for selection; use eplan_object_find. Property placements cannot be selected by these locators.")).ToList();
    }
    public void Validate(string project, IReadOnlyList<DrawingLocator> locators) { MainThread.Assert(); _ = Resolve(project, locators); }
    public bool FocusEditor()
    {
        MainThread.Assert();
        var selection = new SelectionSet { LockProjectByDefault = false, LockSelectionByDefault = false };
        if (selection.CurrentlyEdited is not Page)
        { return false; }
        using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
        using var edit = new Edit();
        return edit.SetFocusToGED();
    }
    public void Apply(string? project, IReadOnlyList<DrawingLocator> locators, bool showPage)
    {
        MainThread.Assert();
        using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
        using var edit = new Edit();
        if (project is null)
        { edit.ClearSelection(); return; }
        List<StorableObject> objects = Resolve(project, locators);
        if (showPage)
        { new EplanPageEditor().Open(project, locators[0].Page); }
        var selection = new SelectionSet { LockProjectByDefault = false, LockSelectionByDefault = false };
        if (selection.GetCurrentProject(false)?.ProjectFullName is not { } current || !ProjectResolver.SamePath(current, project))
        { throw new BridgeErrorException(ErrorReason.PolicyDenied, "EPLAN did not make the target current. Check the page editor."); }
        var ids = new StringCollection();
        foreach (StorableObject obj in objects)
        { string id = obj.ToStringIdentifier(); ids.Add(id.Substring(id.IndexOf('/') + 1)); }
        edit.SelectObjects(EplanProjects.ByPath(project).ProjectLinkFilePath, ids, true);
    }
}
