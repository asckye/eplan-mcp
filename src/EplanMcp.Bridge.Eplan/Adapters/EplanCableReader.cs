using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads one cable for cable.get. Main thread, inside the dispatcher's LockingStep; the project is found by path (A11).
/// S21 read cables found by category as <see cref="Cable"/> objects with parts, cores and ends (api-notes A24). Left to
/// the Large tests (tool spec 5): the cable properties' texts, how long GetSourcesAndTargets takes, whether
/// CableConnections holds a shield's connections, and whether CONNECTION_WIRENUMBER is a colour on cores too.
/// </summary>
internal sealed class EplanCableReader : ICableReader
{
    public CableRecord? ReadCable(string projectPath, string tag, string? language)
    {
        MainThread.Assert();
        TextLanguage textLanguage = EplanText.Language(language);
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        if (EplanDeviceReader.FindMain(finder, tag, IsCable) is not { } main)
        {
            return null;
        }

        var record = new CableRecord { Category = main.FunctionCategory.ToString() };
        if (!IsCable(main))
        {
            return record;
        }

        var cable = (Cable)main;

        // This overload names devices without connection points, as the connection overview report does (EPLAN XML
        // docs); S21 got functions from it (=F+B1-SG1, the terminal +A2-XG4:2), so only functions are named.
        var sources = new List<StorableObject>();
        var targets = new List<StorableObject>();
        cable.GetSourcesAndTargets(sources, targets);
        record.Cable = new CableInfo
        {
            Tag = main.Name ?? string.Empty,
            VisibleTag = main.VisibleName,
            Page = main.Page?.Name,
            Type = EplanText.Read(cable.Properties.FUNC_CABLETYPE, textLanguage),
            WireCount = EplanText.Read(cable.Properties.FUNC_CABLEWIRECOUNT, textLanguage),
            CrossSection = EplanText.Read(cable.Properties.FUNC_CABLEWIRECROSSSECTION, textLanguage),
            Length = EplanText.Read(cable.Properties.FUNC_CABLELENGTH_FULL, textLanguage),
            Sources = Names(sources),
            Targets = Names(targets),
            Parts = [.. main.ArticleReferences.Select(reference => reference.PartNr ?? string.Empty)],
        };
        record.Cores = [.. cable.CableConnections.Select(connection => Core(connection, textLanguage))];
        return record;
    }

    /// <summary>A shielding is a Shield of category Shielding, not a cable (tool spec 5).</summary>
    private static bool IsCable(Function function) => function is Cable && function.FunctionCategory == FunctionCategory.Cable;

    private static ConnectionRecord Core(Connection connection, TextLanguage language)
    {
        ConnectionRecord core = EplanConnectionReader.Record(connection, language);
        core.Designation = EplanText.Read(connection.Properties.CONNECTION_DESIGNATION, language);
        return core;
    }

    private static List<string> Names(List<StorableObject> objects) =>
        [.. objects.OfType<Function>().Select(function => function.Name ?? string.Empty).Where(name => name.Length > 0)];
}
