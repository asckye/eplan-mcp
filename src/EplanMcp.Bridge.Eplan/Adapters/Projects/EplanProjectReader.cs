using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads a project's overview. Main thread, inside the dispatcher's LockingStep. One finder call per kind of object,
/// and one per page for the functions (A170); no counts are cached (api-notes A11).
/// </summary>
internal sealed class EplanProjectReader : IProjectReader
{
    public ProjectContent Read(string projectPath, bool includeProperties, string? language)
    {
        MainThread.Assert();
        TextLanguage textLanguage = EplanText.Language(language);
        Project project = EplanProjects.ByPath(projectPath);
        using var finder = new DMObjectsFinder(project);

        // A null filter returns every page (S6). Functions need the page by page read as well (A170); a tag can then have
        // several main functions (A12), so devices are counted by tag, as eplan_device_list counts them.
        Page[] pages = EplanNativePreconditions.Valid(finder.GetPages(null)).ToArray();
        Function[] functions = EplanDeviceReader.AllFunctions(finder);
        return new ProjectContent
        {
            PageTypes = [.. pages.Select(page => page.PageType.ToString())],
            Devices = functions.Where(function => DeviceTags.IsDevice(function.IsMainFunction, function.Name)).Select(function => function.Name).Distinct(StringComparer.Ordinal).Count(),
            Connections = finder.GetConnections(null).Length,
            PartReferences = finder.GetArticleReferences(null).Length,
            Properties = includeProperties ? EplanProperties.Read(project, textLanguage) : null,
        };
    }
}
