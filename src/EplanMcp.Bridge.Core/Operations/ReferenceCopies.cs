using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Owns scalar copy handles, confined to the dispatcher's main thread; never retains EPLAN objects.</summary>
public sealed class ReferenceCopies(IProjectCatalog catalog, IProjectFiles files, IReferenceProjects projects)
{
    private readonly Dictionary<string, (string Path, uint Database)> _owned = new(StringComparer.Ordinal);

    public static bool ValidPaths(ReferencePrepareArgs args) =>
        ProjectPaths.IsPlainFullPath(args.Source) && ProjectPaths.IsPlainFullPath(args.Destination) && ProjectPaths.IsPlainFullPath(args.ScratchRoot)
        && (args.Source.EndsWith(".elk", StringComparison.OrdinalIgnoreCase) || args.Source.EndsWith(".zw1", StringComparison.OrdinalIgnoreCase))
        && args.Destination.EndsWith(".elk", StringComparison.OrdinalIgnoreCase)
        && Path.GetFullPath(args.Destination).StartsWith(Path.GetFullPath(args.ScratchRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        && !Path.GetFullPath(args.Destination).StartsWith(Path.ChangeExtension(Path.GetFullPath(args.Source), ".edb") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public ReferenceCopyData Prepare(ReferencePrepareArgs args)
    {
        if (!ValidPaths(args))
        { throw Invalid(); }
        CheckPath(args.Source);
        CheckPath(args.Destination);
        string folder = Path.GetDirectoryName(args.Destination)!;
        if (!File.Exists(args.Source) || !Directory.Exists(folder) || Directory.EnumerateFileSystemEntries(folder).Any())
        { throw Invalid(); }
        IReadOnlyList<OpenProject> before = catalog.ListOpenProjects();
        if (before.Any(p => ProjectResolver.SamePath(p.Path, args.Source)))
        { throw Invalid(); }
        if (args.Source.EndsWith(".elk", StringComparison.OrdinalIgnoreCase))
        {
            CheckTree(Path.ChangeExtension(args.Source, ".edb"), closed: true);
            ClosedProject.Check(args.Source, files);
        }
        projects.Copy(args.Source, args.Destination); // A31: never restore over an existing project.
        CheckTree(Path.ChangeExtension(args.Destination, ".edb"));
        ClosedProject.Check(args.Destination, files); // A35/A36: retain and require User.eod/eox.
        string version = files.Compatibility(args.Destination).DatabaseVersion;
        uint database = projects.OpenReadOnly(args.Destination);
        string handle = Guid.NewGuid().ToString("N");
        _owned.Add(handle, (args.Destination, database));
        return new ReferenceCopyData
        {
            Handle = handle,
            Path = args.Destination,
            DatabaseVersion = version,
            CurrentBefore = before.FirstOrDefault(p => p.Current)?.Path,
            CurrentAfter = catalog.ListOpenProjects().FirstOrDefault(p => p.Current)?.Path
        };
    }
    public ProjectListData Close(ReferenceCloseArgs args)
    {
        if (string.IsNullOrEmpty(args.Handle) || !_owned.TryGetValue(args.Handle, out var copy))
        { throw Invalid(); }
        CheckPath(copy.Path);
        CheckTree(Path.ChangeExtension(copy.Path, ".edb"));
        projects.Close(copy.Path, copy.Database);
        _owned.Remove(args.Handle);
        return new ProjectListData { Projects = [.. catalog.ListOpenProjects()] };
    }
    private static void CheckPath(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            { throw Invalid(); }
        }
    }
    private static void CheckTree(string path, bool closed = false)
    {
        CheckPath(path);
        if (!Directory.Exists(path) || (Path.GetExtension(path).ToLowerInvariant() is ".edb" or ".sdb" or ".fdb"
            && (!File.Exists(Path.Combine(path, "User.eod")) || !File.Exists(Path.Combine(path, "User.eox")))))
        { throw Invalid(); }
        foreach (string entry in Directory.EnumerateFileSystemEntries(path))
        {
            CheckPath(entry);
            if (closed && entry.EndsWith(".lck", StringComparison.OrdinalIgnoreCase))
            { throw Invalid(); }
            if (Directory.Exists(entry))
            { CheckTree(entry, closed); }
        }
    }
    private static BridgeErrorException Invalid() => new(ErrorReason.InvalidArgument, "Reference copies require a closed source, local paths without links, an empty scratch folder and an owned close handle.");
}
