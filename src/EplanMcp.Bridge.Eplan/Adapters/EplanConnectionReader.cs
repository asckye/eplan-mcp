using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.E3D;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads connections into plain records for connection.list. Main thread, inside the dispatcher's LockingStep; the
/// project is found by path (api-notes A11). S6 fetched all 2060 connections of the sample project in 21–30 ms and
/// compared their pages in 1–2 ms, so narrowing in memory is cheap. Only the objects this class creates are disposed,
/// as in the rest of the adapter layer.
/// </summary>
internal sealed class EplanConnectionReader : IConnectionReader
{
    public IReadOnlyList<ConnectionRecord>? ReadConnections(string projectPath, string? device, string? page)
    {
        MainThread.Assert();

        // Cross-section and wire number are language-independent values (S6: ??_??@1,5;), which GetStringToDisplay
        // shows the same in any language.
        ISOCode.Language language = EplanText.Language(null);
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        Page? target = null;
        if (page is not null)
        {
            target = EplanDeviceReader.FindPage(finder, page);
            if (target is null)
            {
                return null;
            }
        }

        IEnumerable<Connection> found = device is null ? finder.GetConnections(null) : DeviceConnections(finder, device);
        if (target is not null)
        {
            // In memory by Connection.Page, as S6 found right (40 on =GAA+A1&EFS1/1): the obsolete ConnectionsFilter.Page
            // missed 5 of them. StorableObject overrides Equals, and S6 compared pages this way.
            found = found.Where(connection => target.Equals(connection.Page));
        }

        return [.. found.Select(connection => Record(connection, language))];
    }

    /// <summary>
    /// The connections of the device's main function and of each auxiliary function: ConnectionsFilter.Function matches
    /// connections that start or end at that one function (EPLAN XML docs), and contacts and connection points are
    /// auxiliary functions with connections of their own. A connection between two of them is kept once; StorableObject
    /// overrides Equals and GetHashCode (reflection), and S6 deduplicated a page's connections this way (40 of 40).
    /// </summary>
    private static List<Connection> DeviceConnections(DMObjectsFinder finder, string tag)
    {
        var connections = new List<Connection>();
        if (EplanDeviceReader.FindMain(finder, tag) is not { } main)
        {
            return connections;
        }

        var seen = new HashSet<Connection>();
        foreach (Function function in main.SubFunctions.Prepend(main))
        {
            using var filter = new ConnectionsFilter();
            filter.Function = function;
            foreach (Connection connection in finder.GetConnections(filter))
            {
                if (seen.Add(connection))
                {
                    connections.Add(connection);
                }
            }
        }

        return connections;
    }

    /// <summary>
    /// A Connection3D throws NotSupportedException on StartPin and EndPin and has no page (S6), so only its properties are
    /// read. StartPin and EndPin are null on a side that is not connected (EPLAN XML docs).
    /// </summary>
    private static ConnectionRecord Record(Connection connection, ISOCode.Language language)
    {
        var record = new ConnectionRecord
        {
            Is3D = connection is Connection3D,
            CrossSection = EplanText.Read(connection.Properties.CONNECTION_WIRECROSSSECTION, language),
            Colour = EplanText.Read(connection.Properties.CONNECTION_WIRENUMBER, language),
        };
        if (record.Is3D)
        {
            return record;
        }

        Pin? start = connection.StartPin;
        Pin? end = connection.EndPin;
        record.StartDevice = start?.ParentFunction?.Name;
        record.StartPin = start?.Name;
        record.EndDevice = end?.ParentFunction?.Name;
        record.EndPin = end?.Name;
        record.Page = connection.Page?.Name;
        return record;
    }
}
