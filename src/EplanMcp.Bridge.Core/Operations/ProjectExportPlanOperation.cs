using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The plan of an export of a whole project (<see cref="OperationNames.ExportPxfPlan"/>,
/// <see cref="OperationNames.ExportPartsPlan"/>): checks the arguments against
/// the project without exporting, so that the Host creates a folder only for an export that can run, and names the
/// project, which names the folder (docs/tools/eplan_export_pdf.md, section 5). Reads only.
/// </summary>
public sealed class ProjectExportPlanOperation(ProjectExportKind kind, IProjectCatalog catalog) : Operation<ExportArgs, ExportPlanData>
{
    public override string Name => ProjectExportRunOperation.PlanName(kind);

    protected override ExportPlanData Execute(ExportArgs args) =>
        new() { Project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project).Name };
}
