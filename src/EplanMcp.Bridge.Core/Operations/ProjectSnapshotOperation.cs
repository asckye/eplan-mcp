using System.Diagnostics;
using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.ProjectSnapshot"/> (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.1): pages in ascending
/// order of their numbers, page by page until the time limit or the object limit is reached, at least one page per call,
/// so EPLAN's main thread is never held long and a chunk stays far below the pipe's 16 MB frame. Reads only.
/// </summary>
public sealed class ProjectSnapshotOperation(IProjectCatalog catalog, ISnapshotReader reader, IProjectFiles files, Func<long>? milliseconds = null)
    : Operation<ProjectSnapshotArgs, ProjectSnapshotData>
{
    public const int DefaultSeconds = 10;

    /// <summary>Below the Host's default call timeout of 30 seconds.</summary>
    public const int MaxSeconds = 20;

    /// <summary>About 5 MB of JSON: the sample project's 5,500 objects make one chunk.</summary>
    public const int MaxObjects = 10_000;

    public const int MaxPageNames = 1000;

    private static readonly Stopwatch _clock = Stopwatch.StartNew();

    public override string Name => OperationNames.ProjectSnapshot;

    protected override ProjectSnapshotData Execute(ProjectSnapshotArgs args)
    {
        string profile = Given(args.Profile) ?? SnapshotProfiles.StandardName;
        IReadOnlyDictionary<int, SnapshotProperty> registered = SnapshotCanonical.Registered(profile)
            ?? throw InvalidArgument($"'profile' must be {SnapshotProfiles.StandardName}, the only profile so far.");
        int seconds = args.TimeLimitSeconds ?? DefaultSeconds;
        if (seconds is < 1 or > MaxSeconds)
        {
            throw InvalidArgument($"'timeLimitSeconds' must be 1 to {MaxSeconds}.");
        }

        if (args.Pages is { Count: 0 or > MaxPageNames })
        {
            throw InvalidArgument($"'pages' must name 1 to {MaxPageNames} pages, or be omitted for every page.");
        }

        Func<long> now = milliseconds ?? (() => _clock.ElapsedMilliseconds);
        long start = now();
        using ISnapshotSource source = Open(args);
        List<KeyValuePair<long, string>> pages = [.. source.Pages.OrderBy(p => p.Key)];
        List<string> missing = [];
        if (args.Pages is { } wanted)
        {
            var names = new HashSet<string>(wanted.Select(name => name?.Trim() ?? string.Empty), StringComparer.Ordinal);
            pages = [.. pages.Where(p => names.Contains(p.Value))];
            missing = [.. names.Except(pages.Select(p => p.Value), StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal)];
        }

        List<KeyValuePair<long, string>> todo = [.. pages.Where(p => args.AfterPage is not { } after || p.Key > after)];
        var data = new ProjectSnapshotData
        {
            Project = source.Name,
            ProjectPath = source.Path,
            ProjectGuid = source.ProjectGuid,
            Modified = SnapshotValues.Hint(source.ModifiedDate, source.ModifiedTime),
            Profile = profile,
            Format = SnapshotProfiles.Format,
            PageCount = pages.Count,
            MissingPages = missing,
        };
        int objects = 0;
        foreach (KeyValuePair<long, string> head in todo)
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            if (data.Pages.Count > 0 && (now() - start >= seconds * 1000L || objects >= MaxObjects))
            {
                data.NextAfterPage = data.Pages[data.Pages.Count - 1].Oid;
                break;
            }

            IReadOnlyList<SnapshotSourceObject> read = source.ReadPage(head.Key, profile);
            SnapshotPage page = SnapshotCanonical.Page(profile, read[0], read.Skip(1), registered);
            data.Pages.Add(page);
            objects += page.Objects.Count;
            int done = pages.Count - todo.Count + data.Pages.Count;
            OperationContext.Current.ReportProgress(done, pages.Count, string.Format(CultureInfo.InvariantCulture, "Snapshot: {0} of {1} pages", done, pages.Count));
        }

        data.Complete = data.NextAfterPage is null;
        if (data.Complete && args.Pages is null && args.AfterPage is null)
        {
            data.SnapshotHash = SnapshotCanonical.SnapshotHash(profile, data.Pages);
        }

        data.SkippedConnections = source.SkippedConnections;
        data.Milliseconds = now() - start;
        return data;
    }

    /// <summary>
    /// The open project the argument names; with <see cref="ProjectSnapshotArgs.OpenReadOnly"/> and no such open
    /// project, the checked project file opened read-only (api-notes A34: never open a project that is open already).
    /// </summary>
    private ISnapshotSource Open(ProjectSnapshotArgs args)
    {
        OpenProject project;
        try
        {
            project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        }
        catch (BridgeErrorException ex) when (args.OpenReadOnly && ex.Error.Reason == ErrorReason.ProjectNotOpen)
        {
            return reader.OpenReadOnly(ClosedProject.Check(args.Project, files));
        }

        return reader.Open(project.Path);
    }
}
