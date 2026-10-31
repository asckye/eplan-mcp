using EplanMcp.Contracts;
using Newtonsoft.Json;
namespace EplanMcp.Bridge.Core.Operations;
/// <summary>Validates the complete target before UI changes and independently verifies the resulting selection.</summary>
public sealed class SelectionOperation(IProjectCatalog catalog, ISelectionAdapter? adapter, SelectionKind kind) : Operation<SelectionArgs, SelectionData>
{
    public override string Name => kind == SelectionKind.Select ? OperationNames.SelectionSet : OperationNames.SelectionGet;
    public void Validate(string project, List<DrawingLocator> locators)
    {
        if (!SelectionArgs.Valid(locators, locators.Count == 0 ? "clear" : "replace") || locators.Any(l => !ProjectResolver.SamePath(project, l.Project)))
        { throw InvalidArgument("Supply up to 200 unique discovery locators on one page of the action's explicit project."); }
        ISelectionAdapter ui = adapter ?? throw new BridgeErrorException(ErrorReason.CapabilityMissing, "Selection requires an EPLAN window with the add-in.");
        if (locators.Count > 0)
        { ui.Validate(project, locators); }
    }
    protected override SelectionData Execute(SelectionArgs args)
    {
        bool set = kind == SelectionKind.Select;
        if (args.Offset < 0 || !set && (args.Mode is not null || args.Locators?.Count != 0 || args.ShowPage)
            || set && (!SelectionArgs.Valid(args.Locators, args.Mode!) || args.Mode == "clear" && args.ShowPage || args.Offset != 0))
        { throw InvalidArgument("Supply up to 200 unique same-page locators; replace/add need objects, clear needs no objects or show_page; set has no offset."); }
        ISelectionAdapter ui = adapter ?? throw new BridgeErrorException(ErrorReason.CapabilityMissing, "Selection requires an EPLAN window with the add-in.");
        if (set)
        {
            var desired = args.Locators.ToList();
            string? project = desired.Count == 0 ? null : ProjectResolver.Resolve(catalog.ListOpenProjects(), desired[0].Project).Path;
            if (project is not null)
            { ui.Validate(project, desired); }
            if (args.Mode == "add")
            {
                SelectionData before = ui.Read();
                if (before.Unsupported > 0 || before.Objects.Any(o => !ProjectResolver.SamePath(o.Locator.Project, project!) || o.Locator.Page != desired[0].Page))
                { throw new BridgeErrorException(ErrorReason.PolicyDenied, "The current selection cannot be added on this page. Use replace or clear."); }
                desired.AddRange(before.Objects.Where(o => !desired.Any(l => DrawingLocators.Matches(l, o.Locator))).Select(o => o.Locator));
                if (!SelectionArgs.Valid(desired, "add"))
                { throw InvalidArgument("The combined selection exceeds the locator limits; use replace."); }
            }
            if (project is not null && !args.ShowPage && !catalog.ListOpenProjects().Any(p => p.Current && ProjectResolver.SamePath(p.Path, project)))
            { throw new BridgeErrorException(ErrorReason.PolicyDenied, "Target is not current; use show_page to open its editor."); }
            ui.Apply(project, desired, args.ShowPage);
            SelectionData actual = ui.Read();
            actual.Unselected = desired.Where(l => !actual.Objects.Any(o => DrawingLocators.Matches(l, o.Locator))).ToList();
            actual.Exact = actual.Unsupported == 0 && actual.Unselected.Count == 0 && actual.Objects.Count == desired.Count;
            actual.Total = actual.Objects.Count;
            actual.Objects = actual.Objects.Take(200).ToList();
            return actual;
        }
        SelectionData data = ui.Read();
        data.Total = data.Objects.Count;
        data.Fingerprint = SnapshotCanonical.Sha256(JsonConvert.SerializeObject(data));
        data.Objects = data.Objects.Skip(args.Offset).Take(200).ToList();
        return data;
    }
}
