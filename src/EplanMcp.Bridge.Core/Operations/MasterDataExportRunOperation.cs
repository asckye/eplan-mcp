using System.Globalization;
using System.Xml;
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
        int? inFile = request.Format == MasterDataFormat.Xml ? PartsInFile(path) : null;
        return new ExportRunData
        {
            Database = PartSearchOperation.ForModel(exported.Database),
            PartCount = inFile ?? exported.PartCount,
            VariantCount = exported.VariantCount,
            Warning = request.Format == MasterDataFormat.Xml && inFile is null ? "partCount was counted in the database: the file's count of parts could not be read." : null,
        };
    }

    /// <summary>
    /// The parts an XML file has, as its root <c>partsmanagement</c> counts them in <c>count</c> (S24 r13): a part with
    /// several variants counts once there (Large test 2026-10-21: 454, where <c>Parts</c> gave 464); null when unreadable.
    /// </summary>
    private static int? PartsInFile(string path)
    {
        try
        {
            using XmlReader reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            return reader.MoveToContent() == XmlNodeType.Element && reader.LocalName == "partsmanagement"
                && int.TryParse(reader.GetAttribute("count"), NumberStyles.None, CultureInfo.InvariantCulture, out int count)
                ? count
                : null;
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
