using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.ReportUpdatePlan"/>: which report pages EPLAN will create, replace or remove is known only
/// after running (M4 design Q7), so the plan names the scope (docs/tools/eplan_report_update.md, section 5): for all
/// reports one <c>update</c> of the project's <c>reports</c> with the report pages now; for given pages one
/// <c>update</c> of each page's <c>report</c>. The details give the report page count. Reads only.
/// </summary>
public sealed class ReportUpdatePlanOperation(IProjectCatalog catalog, IPageReader reader, IReportUpdater updater) : Operation<ReportUpdateArgs, WritePlan>
{
    /// <summary>The <see cref="WritePlan.Details"/> key of how many report pages the project has now.</summary>
    public const string ReportPagesDetail = "reportPages";

    public override string Name => OperationNames.ReportUpdatePlan;

    protected override WritePlan Execute(ReportUpdateArgs args)
    {
        var request = ReportUpdateRequest.Check(args, catalog, reader, updater);
        string count = request.ReportPages.Count.ToString(CultureInfo.InvariantCulture);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = request.Pages is null
                ? [new PlannedChange { Action = "update", Kind = "project", Name = request.Project.Name, Property = "reports", OldValue = count }]
                : [.. request.Pages.Select(page => new PlannedChange { Action = "update", Kind = "page", Name = page, Property = "report" })],
            Fingerprint = request.StateFingerprint(),
            Details = new Dictionary<string, string> { [ReportPagesDetail] = count },
        };
    }
}
