using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>Typed navigation only; open editors refresh automatically after commits (S42/A54).</summary>
internal sealed class EplanPageEditor : IPageEditor
{
    public string? Open(string projectPath, string pageName)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        using var finder = new DMObjectsFinder(project);
        Page? page = EplanDeviceReader.FindPage(finder, pageName);
        if (page is null)
        { return null; }
        using var edit = new Edit();
        Page? opened = edit.OpenPageWithName(project.ProjectLinkFilePath, page.Name);
        if (opened is null || !string.Equals(opened.Name, pageName, StringComparison.Ordinal)
            || !string.Equals(opened.Project.ProjectFullName, project.ProjectFullName, StringComparison.OrdinalIgnoreCase))
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed,
                "EPLAN did not confirm opening the requested page. Check the editor and EPLAN system messages.");
        }
        return opened.Name;
    }
}
