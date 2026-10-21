using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The plan of an export of a whole project (<see cref="OperationNames.ExportPxfPlan"/>,
/// <see cref="OperationNames.ExportPartsPlan"/>, <see cref="OperationNames.ExportLabelsPlan"/>,
/// <see cref="OperationNames.ExportUntranslatedPlan"/>): checks the arguments
/// against the project and EPLAN's settings without exporting, so that the Host creates a folder only for an export that
/// can run, and names the project, which names the folder, and for labels the scheme and the file's extension
/// (docs/tools/eplan_export_pdf.md, section 5). Reads only.
/// </summary>
public sealed class ProjectExportPlanOperation(ProjectExportKind kind, IProjectCatalog catalog, IProjectExporter exporter) : Operation<ExportArgs, ExportPlanData>
{
    public override string Name => ProjectExportRunOperation.PlanName(kind);

    protected override ExportPlanData Execute(ExportArgs args)
    {
        var request = ProjectExportRequest.Check(kind, args, catalog, exporter);
        return new ExportPlanData
        {
            Project = request.Project.Name,
            Scheme = request.Scheme,
            Extension = request.Extension,
        };
    }
}
