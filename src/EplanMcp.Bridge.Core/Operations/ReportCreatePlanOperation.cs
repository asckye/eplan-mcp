using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Validates the report settings and fingerprints the page inventory (A84).</summary>
public sealed class ReportCreatePlanOperation(IProjectCatalog catalog, IPageReader reader, IReportCreator creator) : Operation<ReportCreateArgs, WritePlan>
{
    public override string Name => OperationNames.ReportCreatePlan;
    protected override WritePlan Execute(ReportCreateArgs args)
    {
        var errors = args.Validate();
        if (PageNames.Parse(args.FirstPage, out string? problem) is null)
        { errors.Add("'first_page' " + problem); }
        if (errors.Count > 0)
        { throw WriteArguments.Invalid(errors); }
        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        IReadOnlyList<string> pages = reader.PageNames(project.Path);
        IReadOnlyList<ReportPageRecord> reports = creator.ReportPages(project.Path);
        if (pages.Any(p => string.Equals(p, args.FirstPage, StringComparison.OrdinalIgnoreCase))
            && !reports.Any(p => string.Equals(p.Name, args.FirstPage, StringComparison.OrdinalIgnoreCase)))
        { throw InvalidArgument("'first_page' already exists and is not a report page; choose a new full page name."); }
        return new WritePlan
        {
            Project = project.Name,
            ProjectPath = project.Path,
            Changes = [new() { Action = "create", Kind = "report", Name = args.FirstPage!, Property = args.Type, NewValue = args.Form }],
            Fingerprint = Fingerprint.Of(pages.Concat(reports.Select(p => p.Name + "\t" + p.Id + "\t" + p.Type + "\t" + p.Form))),
            Details = new() { ["filter_scheme"] = args.FilterScheme ?? string.Empty, ["sort_scheme"] = args.SortScheme ?? string.Empty },
        };
    }
}
