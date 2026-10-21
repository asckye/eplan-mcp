using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.E3D;
using Eplan.EplApi.DataModel.EObjects;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads terminal strips for the terminal strip operations. Main thread, inside the dispatcher's LockingStep; the
/// project is found by path (A11). S21 read 45 strips with GetTerminalStrips(null), with their names, terminals and
/// bridges (api-notes A24); the time it takes is measured by the Large tests, and so is whether a strip made of
/// terminals only, without a definition, is among them. No FunctionBase.Properties on a strip: it throws there (EPLAN XML docs).
/// S21 only counted terminals, so the Large tests also check that Terminals is in strip order, that the internal and
/// external connections are the sides of the terminal diagram, and how bridges read through Bridge.Terminals.
/// </summary>
internal sealed class EplanTerminalReader : ITerminalReader
{
    public IReadOnlyList<TerminalStripListItem> ReadStrips(string projectPath)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        return [.. finder.GetTerminalStrips(null).Select(Item)];
    }

    public TerminalStripRecord? ReadStrip(string projectPath, string name)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        TerminalStrip? strip = finder.GetTerminalStrips(null).FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
        return strip is null ? null : new TerminalStripRecord { Strip = Item(strip), Terminals = [.. strip.Terminals.Select(Read)] };
    }

    private static TerminalStripListItem Item(TerminalStrip strip) => new()
    {
        Name = strip.Name ?? string.Empty,
        Terminals = strip.Terminals.Length,
        Bridges = strip.Bridges.Length,
    };

    private static TerminalItem Read(Terminal terminal) => new()
    {
        Name = terminal.Name ?? string.Empty,
        Main = terminal.IsMainTerminal,
        Page = terminal.Page?.Name,
        Inside = Ends(terminal, terminal.InternalConnections),
        Outside = Ends(terminal, terminal.ExternalConnections),

        // A bridge of a terminal groups its segments; Terminals are all the terminals it connects, this one included (XML).
        BridgedWith = [.. terminal.Bridges.SelectMany(bridge => bridge.Terminals).Where(t => !terminal.Equals(t)).Select(t => t.Name ?? string.Empty)],
        Plc = PlcOf(terminal),
    };

    /// <summary>
    /// The end of each connection (<c>ConnectionInfo.Conn</c>) that is not at this terminal (tool spec 5). A Connection3D
    /// throws on its ends (S6) and is skipped; StorableObject overrides Equals, as the connection reader relies on.
    /// </summary>
    private static List<ConnectionEnd> Ends(Terminal terminal, Terminal.ConnectionInfo[] connections) =>
    [
        .. connections.Select(info => info.Conn).OfType<Connection>().Where(c => c is not Connection3D)
            .Select(c => terminal.Equals(c.StartPin?.ParentFunction) ? c.EndPin : c.StartPin)
            .OfType<Pin>()
            .Select(pin => new ConnectionEnd { Device = pin.ParentFunction?.Name, Pin = pin.Name }),
    ];

    /// <summary>
    /// Terminal.PLC throws WrongCategoryException for a terminal of a strip and BaseException when there is no PLC (EPLAN
    /// XML docs), so strip terminals may never show one; the Large tests report how many do.
    /// </summary>
    private static string? PlcOf(Terminal terminal)
    {
        try
        {
            return terminal.PLC?.Name;
        }
        catch (Exception ex) when (ex is WrongCategoryException or BaseException)
        {
            return null;
        }
    }
}
