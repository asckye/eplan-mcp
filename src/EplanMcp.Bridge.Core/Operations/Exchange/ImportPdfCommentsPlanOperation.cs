using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Validates intake and counts existing comments; new counts cannot be predicted (A39/S44).</summary>
public sealed class ImportPdfCommentsPlanOperation(IProjectCatalog catalog, IPdfCommentsImporter importer)
    : Operation<ImportPdfCommentsArgs, WritePlan>
{
    public override string Name => OperationNames.ImportPdfCommentsPlan;

    protected override WritePlan Execute(ImportPdfCommentsArgs args)
    {
        var request = ImportPdfCommentsRequest.Check(args, catalog, importer);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Fingerprint = request.Fingerprint(),
            Changes = [new PlannedChange { Action = "create", Kind = "PDF comments", Name = request.Project.Name,
                NewValue = request.Source.Describe() + "; " + request.Ids.Count.ToString(CultureInfo.InvariantCulture) + " existing; sticky notes only; number of new comments unknown" }],
            Details = new Dictionary<string, string>
            {
                ["file"] = request.Source.File,
                ["sha256"] = request.Source.Sha256,
                ["bytes"] = request.Source.Bytes.ToString(CultureInfo.InvariantCulture),
                ["before"] = request.Ids.Count.ToString(CultureInfo.InvariantCulture)
            },
        };
    }
}
