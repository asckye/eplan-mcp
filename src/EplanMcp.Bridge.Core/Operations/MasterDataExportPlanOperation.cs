using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The plan of a database export (<see cref="OperationNames.PartExportPlan"/>,
/// <see cref="OperationNames.TranslationExportPlan"/>): checks the arguments, and for parts that every part number is in
/// the configured database, without exporting, so that the Host creates a folder only for an export that can run
/// (docs/tools/eplan_part_export.md, section 7: a missing part leaves no folder). Names the database and the file's
/// extension; a backup only of a file database. Reads only.
/// </summary>
public sealed class MasterDataExportPlanOperation(MasterDataExportKind kind, IMasterDataExporter exporter) : Operation<ExportArgs, ExportPlanData>
{
    public override string Name => MasterDataExportRunOperation.PlanName(kind);

    protected override ExportPlanData Execute(ExportArgs args)
    {
        var request = MasterDataExportRequest.Check(kind, args);
        string database = kind == MasterDataExportKind.Parts
            ? MasterDataExportRequest.AllFound(exporter.ExportParts(request.Parts, request.Format, null)).Database
            : exporter.TranslationDatabase();
        MasterDataExportRequest.CheckBackupOf(request, database);
        return new ExportPlanData { Database = PartSearchOperation.ForModel(database), Extension = request.Extension };
    }
}
