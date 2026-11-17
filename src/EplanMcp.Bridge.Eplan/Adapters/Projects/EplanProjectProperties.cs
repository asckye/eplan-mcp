using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>The title block properties of eplan_project_update, whose types come from the EPLAN XML (ADR-0052).</summary>
internal sealed class EplanProjectProperties : IProjectProperties
{
    public string Read(string projectPath, string key)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        using ProjectPropertyList list = project.Properties;
        using AnyPropertyId id = Id(project, key);
        using PropertyValue value = list[id];
        if (value.IsEmpty)
        { return string.Empty; }
        if (!Multilingual(key))
        { return value.ToString(); }
        using MultiLangString text = value.ToMultiLangString();
        return text.GetAsString();
    }

    public void Write(string projectPath, string key, string value)
    {
        MainThread.Assert();
        try
        {
            Project project = EplanProjects.ByPath(projectPath);
            using ProjectPropertyList list = project.Properties;
            using AnyPropertyId id = Id(project, key);
            if (!Multilingual(key))
            {
                using PropertyValue plain = value;
                list[id] = plain;
                return;
            }
            using var text = new MultiLangString();
            if (TextLanguages.IsSerialized(value))
            { text.SetAsString(value); }
            else if (value.Length > 0)
            { text.AddString(ISOCode.Language.L___, value); }
            using PropertyValue multilingual = text;
            list[id] = multilingual;
        }
        catch (BaseException ex)
        {
            WriteExceptionDiagnostics.Report(ex);
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN did not set a project property, so nothing was committed.", ex);
        }
    }

    private static bool Multilingual(string key) => !int.TryParse(key, out int number) || ProjectUpdateArgs.TitleBlock[number];

    private static AnyPropertyId Id(Project project, string key)
    {
        if (int.TryParse(key, out int number))
        { return number; }
        return project.UserDefinedPropertyDefinitions.FirstOrDefault(d => d.IdentifyingName == key)?.Id
            ?? throw new BridgeErrorException(ErrorReason.InvalidArgument, "The project has no user-defined property with this identifying name.", retryable: false, new Dictionary<string, string> { ["property"] = key });
    }
}
