using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The run of a database export (<see cref="OperationNames.PartExportRun"/>; docs/tools/eplan_part_export.md, section
/// 5): checks the arguments again as the plan did, that the Host's folder is there and empty and that the file name is a
/// name only with the plan's extension, then exports into that folder (SEC-20). One EPLAN call, which cannot be
/// interrupted, so a cancel is reported when it has returned (M4 design 4.3). A read: S24 saw no export change its
/// database (api-notes A30).
/// </summary>
public sealed class MasterDataExportRunOperation(MasterDataExportKind kind, IMasterDataExporter exporter) : Operation<ExportArgs, ExportRunData>
{
    public override string Name => Names(kind).Run;

    /// <summary>The name of the plan that goes with the run of <paramref name="kind"/>.</summary>
    public static string PlanName(MasterDataExportKind kind) => Names(kind).Plan;

    private static (string Plan, string Run) Names(MasterDataExportKind kind) => kind switch
    {
        MasterDataExportKind.Parts => (OperationNames.PartExportPlan, OperationNames.PartExportRun),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    protected override ExportRunData Execute(ExportArgs args)
    {
        var request = MasterDataExportRequest.Check(kind, args);
        string file = ProjectExportRunOperation.FileName(args.FileName, request.Extension);
        string path = Path.Combine(ExportFolderCheck.CheckEmpty(args.Directory), file);

        // A part number gone since the plan is reported as the plan reports it; the adapter then exports nothing.
        PartsExport exported = MasterDataExportRequest.AllFound(exporter.ExportParts(request.Parts, request.Format, path));
        OperationContext.Current.ThrowIfCancellationRequested(); // the safe point after the EPLAN call
        return new ExportRunData { Database = PartSearchOperation.ForModel(exported.Database), PartCount = exported.PartCount };
    }
}
