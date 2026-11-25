using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>The parts list settings, from <c>Cfg/PartsListGui.xml</c> (category PROJECT), through <see cref="ProjectSettings"/> as the language settings go.</summary>
internal sealed class EplanReportSettings : IReportSettings
{
    /// <summary>DocumentTypeManager.DocumentType.CircuitSingleLine.</summary>
    private const int SingleLine = 2;

    public bool Read(string projectPath, string setting)
    {
        MainThread.Assert();
        return Run(() =>
        {
            using var settings = new ProjectSettings(EplanProjects.ByPath(projectPath));
            return setting == ReportSettingsArgs.SingleLineCrossReferences
                ? Values(settings, setting).Contains(SingleLine)
                : settings.GetBoolSetting(setting, 0);
        });
    }

    public void Write(string projectPath, string setting, bool value)
    {
        MainThread.Assert();
        Run(() =>
        {
            using var settings = new ProjectSettings(EplanProjects.ByPath(projectPath));
            if (setting == ReportSettingsArgs.SingleLineCrossReferences)
            { WriteSingleLine(settings, setting, value); }
            else
            { settings.SetBoolSetting(setting, value, 0); }
            return true;
        });
    }

    /// <summary>Clears every single-line entry, or fills the first unused one; the list keeps its length.</summary>
    private static void WriteSingleLine(ProjectSettings settings, string setting, bool value)
    {
        int[] values = Values(settings, setting);
        for (int i = 0; i < values.Length; i++)
        {
            if (!value && values[i] == SingleLine)
            { settings.SetNumericSetting(setting, 0, i); }
            else if (value && values[i] == 0)
            {
                settings.SetNumericSetting(setting, SingleLine, i);
                return;
            }
        }
    }

    private static int[] Values(ProjectSettings settings, string setting) =>
        [.. Enumerable.Range(0, settings.GetCountOfValues(setting)).Select(i => settings.GetNumericSetting(setting, i))];

    private static T Run<T>(Func<T> call)
    {
        try
        {
            return call();
        }
        catch (Exception ex) when (ex is BaseException or ArgumentException)
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN refused a report setting: " + ex.Message, ex);
        }
    }
}
