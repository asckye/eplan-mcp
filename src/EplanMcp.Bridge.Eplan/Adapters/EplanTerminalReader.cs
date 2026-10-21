using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads terminal strips for the terminal strip operations. Main thread, inside the dispatcher's LockingStep; the
/// project is found by path (A11). S21 read 45 strips with GetTerminalStrips(null), with their names, terminals and
/// bridges (api-notes A24); the time it takes is measured by the Large tests, and so is whether a strip made of
/// terminals only, without a definition, is among them. No FunctionBase.Properties on a strip: it throws there (EPLAN XML docs).
/// </summary>
internal sealed class EplanTerminalReader : ITerminalReader
{
    public IReadOnlyList<TerminalStripListItem> ReadStrips(string projectPath)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        return [.. finder.GetTerminalStrips(null).Select(Item)];
    }

    private static TerminalStripListItem Item(TerminalStrip strip) => new()
    {
        Name = strip.Name ?? string.Empty,
        Terminals = strip.Terminals.Length,
        Bridges = strip.Bridges.Length,
    };
}
