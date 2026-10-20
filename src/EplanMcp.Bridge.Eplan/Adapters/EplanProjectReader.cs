using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads a project's overview. Main thread, inside the dispatcher's LockingStep. One finder call per kind of object;
/// S6 measured each below 100 ms on the 321-page sample project, so no counts are cached (api-notes A11).
/// </summary>
internal sealed class EplanProjectReader : IProjectReader
{
    public ProjectContent Read(string projectPath, bool includeProperties, string? language)
    {
        MainThread.Assert();
        TextLanguage textLanguage = EplanText.Language(language);
        Project project = EplanProjects.ByPath(projectPath);
        using var finder = new DMObjectsFinder(project);

        // A null filter returns every object of the kind (S6). GetFunctions leaves out connection definition points.
        Page[] pages = finder.GetPages(null);
        Function[] functions = finder.GetFunctions(null);
        return new ProjectContent
        {
            PageTypes = [.. pages.Select(page => page.PageType.ToString())],
            Devices = functions.Count(function => DeviceTags.IsDevice(function.IsMainFunction, function.Name)),
            Connections = finder.GetConnections(null).Length,
            PartReferences = finder.GetArticleReferences(null).Length,
            Properties = includeProperties ? EplanProperties.Read(project.Properties, textLanguage) : null,
        };
    }
}
