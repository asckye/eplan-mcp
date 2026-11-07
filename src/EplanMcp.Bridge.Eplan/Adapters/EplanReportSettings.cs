using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>The parts list settings, from <c>Cfg/PartsListGui.xml</c> (category PROJECT), through <see cref="ProjectSettings"/> as the language settings go.</summary>
internal sealed class EplanReportSettings : IReportSettings
{
    public bool Read(string projectPath, string setting)
    {
        MainThread.Assert();
        return Run(() =>
        {
            using var settings = new ProjectSettings(EplanProjects.ByPath(projectPath));
            return settings.GetBoolSetting(setting, 0);
        });
    }

    public void Write(string projectPath, string setting, bool value)
    {
        MainThread.Assert();
        Run(() =>
        {
            using var settings = new ProjectSettings(EplanProjects.ByPath(projectPath));
            settings.SetBoolSetting(setting, value, 0);
            return true;
        });
    }

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
