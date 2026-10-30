using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.E3D;
using Eplan.EplApi.DataModel.EObjects;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads one PLC box for plc.get. Main thread, inside the dispatcher's LockingStep; the project is found by path (A11).
/// PlcIOEntry was always null in S21, so addresses come from the properties (api-notes A24). The Large tests on the
/// fixture's =K1+A2-KE1 found 34 PLC.PLCTerminals objects with 17 names, the address in FUNC_PLCADDRESS (%QX2.0) and the
/// same text in FUNC_PLCSYMBOLICADDRESS_CALCULATED (tool spec 5).
/// </summary>
internal sealed class EplanPlcReader : IPlcReader
{
    public PlcRecord? ReadPlc(string projectPath, string tag, string? language, bool detailed = false)
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
        Terminal[] channels = plc.PLCTerminals ?? [];
        record.Channels = [.. channels.Select(terminal => Channel(terminal, textLanguage, detailed))];
        if (detailed && record.Channels.Count > 0)
        {
            // A11/A55: a native name-prefix filter includes unplaced and cross-page representations of this card.
            using var filter = new FunctionsFilter { Name = plc.Name + ":", ExactNameMatching = false, FunctionCategory = FunctionCategory.PLCTerminal };
            ILookup<string, Terminal> parentless = finder.GetFunctions(filter).OfType<Terminal>()
                .Where(t => t.PLC is null).ToLookup(t => t.Name, StringComparer.Ordinal);
            foreach (PlcChannel channel in record.Channels)
            { channel.ParentlessRepresentations = parentless[channel.Name].Count(); }
        }
        record.Connections = Connections(finder, plc, channels);
        return record;
    }

    /// <summary>The object of a PLC box is a PLC of category PLCBox (tool spec 5).</summary>
    private static bool IsPlcBox(Function function) => function is PLC && function.FunctionCategory == FunctionCategory.PLCBox;

    private static PlcChannel Channel(Terminal terminal, TextLanguage language, bool detailed)
    {
        using FunctionPropertyList properties = terminal.Properties;
        return Channel(terminal, language, properties, detailed);
    }

    internal static PlcChannel Channel(Terminal terminal, TextLanguage language, FunctionPropertyList properties, bool detailed = true)
    {
        (string? text, string? textLanguage) = EplanText.ReadWithLanguage(properties.FUNC_TEXT, language);
        return new PlcChannel
        {
            Name = terminal.Name ?? string.Empty,
            Page = terminal.Page?.Name,
            Main = terminal.IsMainFunction,
            Owner = detailed ? terminal.PLC?.Name : null,
            CardReference = detailed ? EplanText.Read(properties.FUNC_PLCPAIRCROSSREFERENCE, language) : null,
            ManualSymbolicAddress = detailed ? EplanText.Read(properties.FUNC_PLCSYMBOLICADDRESS_MANUAL, language) : null,
            Address = EplanText.Read(properties.FUNC_PLCADDRESS, language),
            SymbolicAddress = EplanText.Read(properties.FUNC_PLCSYMBOLICADDRESS_CALCULATED, language),
            DataType = EplanText.Read(properties.FUNC_PLCDATATYPE, language),
            Text = text,
            TextLanguage = textLanguage,
        };
    }

    /// <summary>
    /// Each end at the box of the connections of its main function, its auxiliary functions and its channels, found as
    /// connection.list finds a device's (ConnectionsFilter.Function per function). On the fixture that found 22
    /// connections of =K1+A2-KE1, while ConnectionsFilter.Function on the PLC.PLCTerminals objects alone found none (Large
    /// tests, 2026-10-21). An end is matched to its channel object by its function or by one of the channel's Pins
    /// (StorableObject and Pin override Equals); plc.get matches the others by name. A Connection3D has no ends (S6).
    /// </summary>
    private static List<PlcConnectionRecord> Connections(DMObjectsFinder finder, PLC plc, Terminal[] channels)
    {
        List<Function> queried = [plc, .. plc.SubFunctions ?? [], .. channels];
        var atBox = new HashSet<Function>(queried);
        var channelOf = new Dictionary<Function, int>();
        var pinOf = new Dictionary<Pin, int>();
        for (int i = channels.Length - 1; i >= 0; i--)
        {
            // From the last, so that the first of two objects EPLAN sees as equal keeps the entry.
            channelOf[channels[i]] = i;
            foreach (Pin pin in channels[i].Pins ?? [])
            {
                pinOf[pin] = i;
            }
        }

        string tag = plc.Name ?? string.Empty;
        var records = new List<PlcConnectionRecord>();
        foreach (Connection connection in EplanConnectionReader.FunctionConnections(finder, queried).Where(c => c is not Connection3D))
        {
            Pin? start = connection.StartPin;
            Pin? end = connection.EndPin;
            string? page = EplanConnectionReader.PageOf(connection)?.Name;
            foreach ((Pin? at, Pin? other) in new[] { (start, end), (end, start) })
            {
                if (at?.ParentFunction is not { } function || !(atBox.Contains(function) || pinOf.ContainsKey(at) || IsOfTag(function.Name, tag)))
                {
                    continue;
                }

                records.Add(new PlcConnectionRecord
                {
                    Channel = channelOf.TryGetValue(function, out int byFunction) ? byFunction : pinOf.TryGetValue(at, out int byPin) ? byPin : null,
                    Function = function.Name,
                    Page = page,
                    Other = new ConnectionEnd { Device = other?.ParentFunction?.Name, Pin = other?.Name },
                });
            }
        }

        return records;
    }

    /// <summary>Whether <paramref name="name"/> is the box's tag or a connection point of it (<c>=K1+A2-KE1:2.3</c>, A11).</summary>
    private static bool IsOfTag(string? name, string tag) =>
        tag.Length > 0 && name is not null && (name == tag || name.StartsWith(tag + ":", StringComparison.Ordinal));
}
