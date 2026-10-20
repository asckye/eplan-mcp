using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// What generating connections changed on each page (docs/tools/eplan_connection_generate.md, section 3): the
/// connections there after that were not there before, and the other way round. Connections are compared by their two
/// ends, either way round, counting repeats: an update need not keep a connection's object or direction, and S10
/// showed only that it keeps the count on a page whose connections are complete (api-notes A14).
/// </summary>
internal static class ConnectionChanges
{
    /// <summary>The most added and removed connections all pages together list (tool spec 3).</summary>
    public const int MaxListed = 100;

    /// <summary>
    /// Each page's counts and changes. The lists hold <see cref="MaxListed"/> connections in all, the removed ones first:
    /// whether an update removes connections is not verified (tool spec 5), so they are the ones to look at.
    /// </summary>
    public static List<ConnectionGeneratePage> Compare(
        IReadOnlyList<string> pages, IReadOnlyList<IReadOnlyList<ConnectionRecord>> before, IReadOnlyList<IReadOnlyList<ConnectionRecord>> after)
    {
        List<ConnectionRecord>[] removed = [.. pages.Select((_, i) => Missing(before[i], after[i]))];
        List<ConnectionRecord>[] added = [.. pages.Select((_, i) => Missing(after[i], before[i]))];
        List<ConnectionGeneratePage> result =
        [
            .. pages.Select((page, i) => new ConnectionGeneratePage
            {
                Page = page,
                Before = before[i].Count,
                After = after[i].Count,
                AddedCount = added[i].Count,
                RemovedCount = removed[i].Count,
            }),
        ];
        int room = MaxListed;
        for (int i = 0; i < result.Count; i++)
        {
            result[i].Removed = Listed(removed[i], room);
            room -= result[i].Removed.Count;
        }

        for (int i = 0; i < result.Count; i++)
        {
            result[i].Added = Listed(added[i], room);
            room -= result[i].Added.Count;
        }

        return result;
    }

    /// <summary>A connection's two ends, each as function and connection point, in ordinal order; empty names count as none.</summary>
    public static string Key(ConnectionRecord connection)
    {
        string start = connection.StartDevice + "\u001f" + connection.StartPin;
        string end = connection.EndDevice + "\u001f" + connection.EndPin;
        return string.CompareOrdinal(start, end) <= 0 ? start + "\u001e" + end : end + "\u001e" + start;
    }

    /// <summary>The connections of <paramref name="these"/> that <paramref name="others"/> does not have, as often as they are missing.</summary>
    private static List<ConnectionRecord> Missing(IReadOnlyList<ConnectionRecord> these, IReadOnlyList<ConnectionRecord> others)
    {
        Dictionary<string, int> left = others.GroupBy(Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var missing = new List<ConnectionRecord>();
        foreach (ConnectionRecord connection in these)
        {
            string key = Key(connection);
            if (left.TryGetValue(key, out int count) && count > 0)
            {
                left[key] = count - 1;
            }
            else
            {
                missing.Add(connection);
            }
        }

        return missing;
    }

    /// <summary>The first <paramref name="room"/> connections, with their ends as connection.list gives them.</summary>
    private static List<ChangedConnection> Listed(List<ConnectionRecord> connections, int room) =>
    [
        .. connections.Take(room).Select(c => new ChangedConnection
        {
            From = ConnectionListOperation.End(c.StartDevice, c.StartPin),
            To = ConnectionListOperation.End(c.EndDevice, c.EndPin),
        }),
    ];
}
