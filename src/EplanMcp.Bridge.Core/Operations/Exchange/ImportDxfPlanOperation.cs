using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.ImportDxfPlan"/>: checks the page and the file and returns one <c>create</c> change that
/// names the file, its size and SHA-256 for the dry run, the approval form and the audit (docs/tools/eplan_import_dxf.md,
/// section 5; M4c design 4.5). Reads only.
/// </summary>
public sealed class ImportDxfPlanOperation(IProjectCatalog catalog, IPageReader pages, IDrawingImporter importer) : Operation<ImportDxfArgs, WritePlan>
{
    public override string Name => OperationNames.ImportDxfPlan;

    protected override WritePlan Execute(ImportDxfArgs args)
    {
        var request = ImportDxfRequest.Check(args, catalog, pages, importer);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = [request.Change()],
            Fingerprint = request.StateFingerprint(),
            Details = new Dictionary<string, string>
            {
                ["file"] = request.Source.File,
                ["sha256"] = request.Source.Sha256,
                ["bytes"] = request.Source.Bytes.ToString(CultureInfo.InvariantCulture),
                ["scale"] = request.Scale,
                ["position"] = request.Position,
            },
        };
    }
}
