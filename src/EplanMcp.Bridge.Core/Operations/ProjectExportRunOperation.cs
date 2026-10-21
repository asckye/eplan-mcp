using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The run of an export of a whole project (<see cref="OperationNames.ExportPxfRun"/>,
/// <see cref="OperationNames.ExportPartsRun"/>, <see cref="OperationNames.ExportLabelsRun"/>; docs/tools/eplan_export_*.md,
/// section 5): checks the arguments again as the plan did, that the Host's folder is there and empty and that the file
/// name is a name only, with the extension the plan named, then exports into that folder (SEC-20). One EPLAN call, which cannot be interrupted, so a cancel is
/// reported when it has returned (M4 design 4.3). A read: the project's design data does not change (A25).
/// </summary>
public sealed class ProjectExportRunOperation(ProjectExportKind kind, IProjectCatalog catalog, IProjectExporter exporter) : Operation<ExportArgs, ExportRunData>
{
    /// <summary>The extension EPLAN adds to a PXF export (S25); the Host leaves it out of the name.</summary>
    public const string PxfExtension = ".epj";

    public override string Name => Names(kind).Run;

    /// <summary>The name of the plan that goes with the run of <paramref name="kind"/>.</summary>
    public static string PlanName(ProjectExportKind kind) => Names(kind).Plan;

    private static (string Plan, string Run) Names(ProjectExportKind kind) => kind switch
    {
        ProjectExportKind.Pxf => (OperationNames.ExportPxfPlan, OperationNames.ExportPxfRun),
        ProjectExportKind.PartsList => (OperationNames.ExportPartsPlan, OperationNames.ExportPartsRun),
        ProjectExportKind.Labels => (OperationNames.ExportLabelsPlan, OperationNames.ExportLabelsRun),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    protected override ExportRunData Execute(ExportArgs args)
    {
        var request = ProjectExportRequest.Check(kind, args, catalog, exporter);
        string file = FileName(args.FileName, request.Extension);
        string path = Path.Combine(ExportFolderCheck.CheckEmpty(args.Directory), file);
        string projectPath = request.Project.Path;

        // Always a file in the Host's folder, never a target of EPLAN's own (SEC-20): a labelling scheme's default
        // target lies below $(MD_DOCUMENTS) (S25).
        switch (kind)
        {
            case ProjectExportKind.Pxf:
                exporter.ExportPxf(projectPath, path, args.IncludeMasterData);
                break;
            case ProjectExportKind.PartsList:
                exporter.ExportPartsList(projectPath, path);
                break;
            default:
                exporter.ExportLabels(projectPath, request.Scheme!, request.Language!, path);
                break;
        }

        OperationContext.Current.ThrowIfCancellationRequested(); // the safe point after the EPLAN call
        return new ExportRunData { Project = request.Project.Name, Scheme = request.Scheme };
    }

    /// <summary>
    /// The file's name in the folder, as the Host checked it; checked again so that EPLAN never gets a path (SEC-20).
    /// A PXF name has no <c>.epj</c>: EPLAN adds it to a name without one (S25), and whether it adds a second one to a
    /// name that has it is not verified (tool spec 5), so the Host never sends one. Other names end with
    /// <paramref name="extension"/>.
    /// </summary>
    private static string FileName(string? fileName, string? extension)
    {
        string name = fileName ?? string.Empty;
        bool plain = name.Length > 0 && name.IndexOfAny(['\\', '/', ':']) < 0 && name.IndexOf("..", StringComparison.Ordinal) < 0;
        return plain && (extension is null
                ? !name.EndsWith(PxfExtension, StringComparison.OrdinalIgnoreCase)
                : name.Length > extension.Length && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            ? name
            : throw InvalidArgument(extension is null
                ? "'fileName' must be a file name without a folder and without the extension .epj."
                : $"'fileName' must be a file name ending in {extension}, without a folder.");
    }
}
