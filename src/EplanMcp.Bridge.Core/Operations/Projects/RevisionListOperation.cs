using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.RevisionList"/> (docs/tools/eplan_revision_list.md): resolves the project and reads its
/// revision state in the same main-thread call. The state comes from the link file's extension and the log from the
/// indexed project properties, as spike S23 r3 found them (api-notes A28); both are worked out here, where they can be
/// tested without EPLAN. Reads only.
/// </summary>
public sealed class RevisionListOperation(IProjectCatalog catalog, IRevisionReader revisions) : Operation<RevisionListArgs, RevisionListData>
{
    /// <summary>The most uncompleted pages the result names.</summary>
    public const int MaxUncompletedPages = 50;

    public const string None = "none";
    public const string Tracking = "tracking";
    public const string Completed = "completed";
    public const string Unknown = "unknown";

    public override string Name => OperationNames.RevisionList;

    protected override RevisionListData Execute(RevisionListArgs args)
    {
        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        RevisionContent content = revisions.Read(project.Path, args.Language);
        string state = StateOf(content.LinkFile);
        List<RevisionItem> log = [.. content.Log.OrderBy(record => record.Index).Select(Item)];
        return new RevisionListData
        {
            Project = project.Name,
            Path = content.LinkFile,
            State = state,
            TrackingStarted = content.TrackingStarted,
            Parent = state == Completed ? content.Parent : null,
            Revisions = Paging.Slice(log, args),
            Total = log.Count,
            UncompletedPages = [.. content.UncompletedPages.Take(MaxUncompletedPages)],
            UncompletedPageCount = content.UncompletedPages.Count,
            MarkedPageCount = content.MarkedPages,
        };
    }

    /// <summary>The revision state by the link file's extension (S23 r3): <c>.elk</c>, <c>.ell</c>, <c>.elr</c>, any case.</summary>
    public static string StateOf(string linkFile) => Extension(linkFile).ToUpperInvariant() switch
    {
        ".ELK" => None,
        ".ELL" => Tracking,
        ".ELR" => Completed,
        _ => Unknown,
    };

    /// <summary>The link file's extension with its dot, or empty.</summary>
    public static string Extension(string linkFile)
    {
        int dot = linkFile.LastIndexOf('.');
        return dot < 0 || dot < linkFile.LastIndexOfAny(['\\', '/']) ? string.Empty : linkFile.Substring(dot);
    }

    /// <summary>
    /// Unix seconds, as S23 r3 read <c>PROJ_REVISION_LOG_DATE</c>, as UTC in ISO 8601; null when not set or not a whole
    /// number of seconds in DateTimeOffset's range.
    /// </summary>
    public static string? UtcDate(string? unixSeconds)
    {
        if (!long.TryParse(unixSeconds?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long seconds)
            || seconds < DateTimeOffset.MinValue.ToUnixTimeSeconds()
            || seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static RevisionItem Item(RevisionRecord record)
    {
        string? date = UtcDate(record.Date);
        return new RevisionItem
        {
            Index = record.Index,
            Name = record.Name,
            Description = record.Description,
            DescriptionLanguage = record.DescriptionLanguage,
            User = record.User,
            Date = date,
            DateRaw = date is null ? record.Date : null,
        };
    }
}
