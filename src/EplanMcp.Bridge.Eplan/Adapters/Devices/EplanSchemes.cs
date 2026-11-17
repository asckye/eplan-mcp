using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// The scheme names under a settings node S21 found for numbering (api-notes A24): user settings through
/// <c>SchemeSetting</c>, project settings through <c>ProjectSchemeSetting</c>, which the EPLAN XML docs describe as the
/// same for a project's settings node. Main thread, inside the dispatcher's LockingStep.
/// </summary>
internal static class EplanSchemes
{
    /// <summary>The schemes of a user settings node such as <c>USER.Terminal.Numbering.Scheme</c>.</summary>
    public static IReadOnlyList<string> OfUser(string node)
    {
        MainThread.Assert();
        using var setting = new SchemeSetting();
        setting.Init(node);
        return Names(setting);
    }

    /// <summary>The schemes of a project settings node such as <c>CableLog.CableNumberingScheme</c>, of the project at <paramref name="projectPath"/>.</summary>
    public static IReadOnlyList<string> OfProject(string node, string projectPath)
    {
        MainThread.Assert();
        using var setting = new ProjectSchemeSetting();
        setting.Init(node, EplanProjects.ByPath(projectPath));
        return Names(setting);
    }

    /// <summary><c>SetScheme(i)</c> leaves the last used scheme as it is, <c>GetName()</c> is the internal name (EPLAN XML docs, B9).</summary>
    private static List<string> Names(SchemeSetting setting)
    {
        var names = new List<string>();
        for (int i = 0; i < setting.GetCount(); i++)
        {
            setting.SetScheme(i);
            names.Add(setting.GetName() ?? string.Empty);
        }

        return names;
    }
}
