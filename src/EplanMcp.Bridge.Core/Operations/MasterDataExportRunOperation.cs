using System.Globalization;
using System.Xml;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The run of a database export (<see cref="OperationNames.PartExportRun"/>, <see cref="OperationNames.TranslationExportRun"/>;
/// docs/tools/eplan_part_export.md, eplan_translation_export.md, section 5): checks the arguments again as the plan did,
/// that the Host's folder is there and empty and that the file name is a name only with the plan's extension, then
/// exports into that folder (SEC-20). One EPLAN call, which cannot be interrupted, so a cancel is reported when it has
/// returned (M4 design 4.3). A read: S24 saw no export change its database (api-notes A30).
/// </summary>
public sealed class MasterDataExportRunOperation(MasterDataExportKind kind, IMasterDataExporter exporter) : Operation<ExportArgs, ExportRunData>
{
    public override string Name => Names(kind).Run;

    /// <summary>The name of the plan that goes with the run of <paramref name="kind"/>.</summary>
    public static string PlanName(MasterDataExportKind kind) => Names(kind).Plan;

    private static (string Plan, string Run) Names(MasterDataExportKind kind) => kind switch
    {
        MasterDataExportKind.Parts => (OperationNames.PartExportPlan, OperationNames.PartExportRun),
        MasterDataExportKind.Translations => (OperationNames.TranslationExportPlan, OperationNames.TranslationExportRun),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    protected override ExportRunData Execute(ExportArgs args)
    {
        var request = MasterDataExportRequest.Check(kind, args);
        string file = ProjectExportRunOperation.FileName(args.FileName, request.Extension);
        string path = Path.Combine(ExportFolderCheck.CheckEmpty(args.Directory), file);
        var data = new ExportRunData();
        if (kind == MasterDataExportKind.Parts)
        {
            // A part number gone since the plan is reported as the plan reports it; the adapter then exports nothing.
            PartsExport exported = MasterDataExportRequest.AllFound(exporter.ExportParts(request.Parts, request.Format, path));
            data.Database = PartSearchOperation.ForModel(exported.Database);
            data.PartCount = exported.PartCount;
            data.VariantCount = exported.VariantCount;
        }
        else
        {
            data.Database = PartSearchOperation.ForModel(exporter.TranslationDatabase());
            if (!exporter.ExportTranslations(path, request.Format))
            {
                throw new BridgeErrorException(
                    ErrorReason.EplanOperationFailed,
                    "EPLAN reported an error exporting its translation database, so no files were kept. Ask the engineer to check the translation database in EPLAN's settings; eplan_message_list may say more.");
            }
        }

        OperationContext.Current.ThrowIfCancellationRequested(); // the safe point after the EPLAN call
        if (kind == MasterDataExportKind.Parts && request.Format == MasterDataFormat.Xml)
        {
            int? inFile = PartsInFile(path);
            data.PartCount = inFile ?? data.PartCount;
            data.Warning = inFile is null ? "partCount was counted in the database: the file's count of parts could not be read." : null;
        }

        return data;
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
