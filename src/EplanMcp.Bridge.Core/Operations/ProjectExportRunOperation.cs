using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The run of an export of a whole project (<see cref="OperationNames.ExportPxfRun"/>,
/// <see cref="OperationNames.ExportPartsRun"/>; docs/tools/eplan_export_pxf.md, eplan_export_parts.md, section 5): finds
/// the project again, checks that the Host's folder is there and empty and that the file name is a name only, with the
/// kind's extension, then exports into that folder (SEC-20). One EPLAN call, which cannot be interrupted, so a cancel is
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
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The extension the Host gives the file; null for PXF, where EPLAN adds it.</summary>
    public static string? Extension(ProjectExportKind kind) => kind switch
    {
        ProjectExportKind.PartsList => ".xml",
        _ => null,
    };

    protected override ExportRunData Execute(ExportArgs args)
    {
        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        string file = FileName(args.FileName, Extension(kind));
        string path = Path.Combine(ExportFolderCheck.CheckEmpty(args.Directory), file);

        // Always a file in the Host's folder, never a target of EPLAN's own (SEC-20).
        if (kind == ProjectExportKind.Pxf)
        {
            exporter.ExportPxf(project.Path, path, args.IncludeMasterData);
        }
        else
        {
            exporter.ExportPartsList(project.Path, path);
        }

        OperationContext.Current.ThrowIfCancellationRequested(); // the safe point after the EPLAN call
        return new ExportRunData { Project = project.Name };
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
