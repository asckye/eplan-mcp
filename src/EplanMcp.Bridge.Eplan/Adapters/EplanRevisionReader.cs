using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads a project's revision state where spike S23 r3 found it (api-notes A28): the link file
/// (<c>Project.ProjectLinkFilePath</c>), <c>PROJ_REVISION_LOG_START</c>, <c>PROJ_REVISIONSPARENT</c>, the indexed
/// <c>PROJ_REVISION_LOG_NAME/DESCRIPTION/USER/DATE[i]</c> from 1 to the first record without a name (at most 1000, EPLAN
/// XML docs), <c>Revision.GetUncompletedPages</c> and every page's <c>PAGE_INSTANCE_WITH_REVISIONMARKER</c>. Main thread,
/// inside the dispatcher's LockingStep; nothing is opened, created or changed. A property that is not set is never
/// read with <c>ToString()</c> (coding standards 9.5, rule 13).
/// </summary>
internal sealed class EplanRevisionReader : IRevisionReader
{
    /// <summary>The indexes the revision log properties allow (EPLAN XML docs: 1 to 1000).</summary>
    private const int MaxRecords = 1000;

    public RevisionContent Read(string projectPath, string? language)
    {
        MainThread.Assert();
        TextLanguage textLanguage = EplanText.Language(language);
        Project project = EplanProjects.ByPath(projectPath);
        ProjectPropertyList properties = project.Properties;
        var content = new RevisionContent
        {
            LinkFile = project.ProjectLinkFilePath ?? string.Empty,
            TrackingStarted = properties.PROJ_REVISION_LOG_START is { IsEmpty: false } started && started.ToBool(),
            Parent = Text(properties.PROJ_REVISIONSPARENT),
        };

        for (int i = 1; i <= MaxRecords && Text(properties.PROJ_REVISION_LOG_NAME[i]) is { Length: > 0 } name; i++)
        {
            (string? description, string? fallback) = EplanText.ReadWithLanguage(properties.PROJ_REVISION_LOG_DESCRIPTION[i], textLanguage);
            content.Log.Add(new RevisionRecord
            {
                Index = i,
                Name = name,
                Description = description,
                DescriptionLanguage = fallback,
                User = Text(properties.PROJ_REVISION_LOG_USER[i]),
                Date = Text(properties.PROJ_REVISION_LOG_DATE[i]),
            });
        }

        // Always empty for a project that was never completed (EPLAN XML docs; S23 r3): the tool says so.
        using (var revision = new Revision())
        {
            content.UncompletedPages = [.. (revision.GetUncompletedPages(project) ?? []).Select(page => page.Name)];
        }

        using var finder = new DMObjectsFinder(project);
        content.MarkedPages = finder.GetPages(null).Count(page => page.Properties.PAGE_INSTANCE_WITH_REVISIONMARKER is { IsEmpty: false } marker && marker.ToBool());
        return content;
    }

    /// <summary>A plain property's text, as S23 r3 read the user, the date and the parent; null when not set.</summary>
    private static string? Text(PropertyValue value) => value.IsEmpty ? null : value.ToString();
}
