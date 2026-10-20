using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The plan of one export tool (<see cref="OperationNames.ExportPdfPlan"/>, <see cref="OperationNames.ExportDxfPlan"/>,
/// <see cref="OperationNames.ExportImagePlan"/>): checks the arguments against the project and EPLAN's schemes without
/// exporting, so that the Host creates a folder only for an export that can run (docs/tools/eplan_export_pdf.md,
/// section 5). Reads only.
/// </summary>
public sealed class ExportPlanOperation(ExportKind kind, IProjectCatalog catalog, IPageReader reader, IExporter exporter) : Operation<ExportArgs, ExportPlanData>
{
    public override string Name => kind switch
    {
        ExportKind.Pdf => OperationNames.ExportPdfPlan,
        ExportKind.Dxf => OperationNames.ExportDxfPlan,
        _ => OperationNames.ExportImagePlan,
    };

    protected override ExportPlanData Execute(ExportArgs args)
    {
        var request = ExportRequest.Check(kind, args, catalog, reader, exporter);
        return new ExportPlanData
        {
            Project = request.Project.Name,
            PageCount = request.Pages.Count,
            Scheme = kind == ExportKind.Image ? null : request.Scheme ?? exporter.LastUsedScheme(kind),
        };
    }
}
