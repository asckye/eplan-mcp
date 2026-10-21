using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads one PLC box for plc.get. Main thread, inside the dispatcher's LockingStep; the project is found by path (A11).
/// S21 read 20 PLC boxes with 34 PLC terminals each, by name only; PlcIOEntry was always null there, so addresses come
/// from the properties (api-notes A24). Left to the Large tests (tool spec 5): whether the properties have values and in
/// what form, which symbolic address property the user interface shows, and whether a channel's connections are found
/// at the PLC terminal itself, as here, or at its connection points.
/// </summary>
internal sealed class EplanPlcReader : IPlcReader
{
    public PlcRecord? ReadPlc(string projectPath, string tag, string? language)
    {
        MainThread.Assert();
        TextLanguage textLanguage = EplanText.Language(language);
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        if (EplanDeviceReader.FindMain(finder, tag, IsPlcBox) is not { } main)
        {
            return null;
        }

        var record = new PlcRecord { Category = main.FunctionCategory.ToString() };
        if (!IsPlcBox(main))
        {
            return record;
        }

        var plc = (PLC)main;
        record.Plc = new PlcInfo
        {
            Tag = main.Name ?? string.Empty,
            VisibleTag = main.VisibleName,
            Page = main.Page?.Name,
            Parts = [.. main.ArticleReferences.Select(reference => reference.PartNr ?? string.Empty)],
            Station = EplanText.Read(plc.Properties.FUNC_PLCSTATIONNAME, textLanguage),
            Rack = EplanText.Read(plc.Properties.FUNC_PLCRACK, textLanguage),
            Slot = EplanText.Read(plc.Properties.FUNC_PLCSOCKET, textLanguage),
            Cpu = plc.IsCPU,
        };
        record.Channels = [.. plc.PLCTerminals.Select(terminal => Channel(finder, terminal, textLanguage))];
        return record;
    }

    /// <summary>The object of a PLC box is a PLC of category PLCBox (tool spec 5).</summary>
    private static bool IsPlcBox(Function function) => function is PLC && function.FunctionCategory == FunctionCategory.PLCBox;

    /// <summary>ConnectionsFilter.Function matches the connections that start or end at that one function (EPLAN XML docs).</summary>
    private static PlcChannel Channel(DMObjectsFinder finder, Terminal terminal, TextLanguage language)
    {
        using var filter = new ConnectionsFilter();
        filter.Function = terminal;
        (string? text, string? textLanguage) = EplanText.ReadWithLanguage(terminal.Properties.FUNC_TEXT, language);
        return new PlcChannel
        {
            Name = terminal.Name ?? string.Empty,
            Address = EplanText.Read(terminal.Properties.FUNC_PLCADDRESS, language),
            SymbolicAddress = EplanText.Read(terminal.Properties.FUNC_PLCSYMBOLICADDRESS_CALCULATED, language),
            DataType = EplanText.Read(terminal.Properties.FUNC_PLCDATATYPE, language),
            Text = text,
            TextLanguage = textLanguage,
            Connected = EplanTerminalReader.OtherEnds(terminal, finder.GetConnections(filter)),
        };
    }
}
