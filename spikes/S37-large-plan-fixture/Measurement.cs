using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.E3D;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S37;

internal sealed class Measurement
{
    internal Dictionary<string, JObject> Functions { get; } = new(StringComparer.Ordinal);
    internal JObject Data { get; } = new();
    internal JObject Summary() => new(Data.Properties().Where(p => p.Name != "pageDetails")
        .Select(p => new JProperty(p.Name, p.Value.DeepClone())));

    internal static Measurement Read(Project project)
    {
        var result = new Measurement();
        var times = new JObject();
        var clock = Stopwatch.StartNew();
        using var finder = new DMObjectsFinder(project);
        Page[] pages = finder.GetPages(null);
        times["getPagesMs"] = clock.Elapsed.TotalMilliseconds;
        result.Data["pages"] = pages.Length;
        clock.Restart();
        Function[] functions = finder.GetFunctions(null);
        times["getFunctionsMs"] = clock.Elapsed.TotalMilliseconds;
        clock.Restart();
        int withoutPage = 0;
        foreach (Function function in functions)
        {
            if (function.Page is null) { withoutPage++; }
            result.Add(function);
        }

        times["finderFunctionMetadataMs"] = clock.Elapsed.TotalMilliseconds;
        result.Data["finderFunctions"] = functions.Length;
        result.Data["finderFunctionsWithPage"] = functions.Length - withoutPage;
        result.Data["finderFunctionsWithoutPage"] = withoutPage;
        clock.Restart();
        result.Data["placements"] = finder.GetPlacements(null).Length;
        times["getPlacementsMs"] = clock.Elapsed.TotalMilliseconds;
        clock.Restart();
        Connection[] connections = finder.GetConnections(null);
        times["getConnectionsMs"] = clock.Elapsed.TotalMilliseconds;
        result.Data["connections"] = connections.Length;
        clock.Restart();
        var perPage = new Dictionary<long, int>();
        int noConnectionPage = 0;
        foreach (Connection connection in connections)
        {
            Page? page = connection is Connection3D ? null : connection.Page;
            if (page is null) { noConnectionPage++; continue; }
            perPage.TryGetValue(page.ObjectIdentifier, out int count);
            perPage[page.ObjectIdentifier] = count + 1;
        }

        times["connectionPageMetadataMs"] = clock.Elapsed.TotalMilliseconds;
        result.Data["connectionsWithoutPageOr3D"] = noConnectionPage;
        clock.Restart();
        var details = new JArray();
        long standard = pages.Length;
        foreach (Page page in pages.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            try
            {
                Placement[] placements = page.AllPlacements;
                Function[] placed = placements.OfType<Function>().ToArray();
                foreach (Function function in placed) { result.Add(function); }
                perPage.TryGetValue(page.ObjectIdentifier, out int pageConnections);
                standard += placed.Length + pageConnections;
                details.Add(new JObject { ["name"] = page.Name, ["placements"] = placements.Length,
                    ["functions"] = placed.Length, ["connections"] = pageConnections,
                    ["standardObjects"] = 1 + placed.Length + pageConnections });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Cannot enumerate page " + page.Name, ex);
            }
        }

        times["allPlacementsAndFunctionUnionMs"] = clock.Elapsed.TotalMilliseconds;
        clock.Restart();
        result.Data["distinctFunctionNames"] = result.Names().Count;
        result.Data["distinctFunctionNamesOrdinal"] = result.Functions.Values.Select(f => (string)f["name"]!)
            .Where(n => n.Length != 0).Distinct(StringComparer.Ordinal).Count();
        string[] duplicateMains = result.Functions.Values.Where(f => (bool)f["main"]! && ((string)f["name"]!).Length != 0)
            .GroupBy(f => (string)f["name"]!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        result.Data["duplicateMainFunctionNames"] = duplicateMains.Length;
        result.Data["duplicateMainFunctionNameSamples"] = new JArray(duplicateMains.Take(30));
        times["distinctFunctionNamesMs"] = clock.Elapsed.TotalMilliseconds;
        result.Data["functionsUnion"] = result.Functions.Count;
        result.Data["standardObjects"] = standard; // Same object kinds as standard snapshots; no property values read.
        result.Data["standardObjectsPerPage"] = pages.Length == 0 ? 0 : (double)standard / pages.Length;
        result.Data["pageDetails"] = details;
        result.Data["timings"] = times;
        return result;
    }

    internal HashSet<string> Names() => new(Functions.Values.Select(f => (string)f["name"]!)
        .Where(n => n.Length != 0), StringComparer.OrdinalIgnoreCase);

    private void Add(Function function) => Functions[function.ToStringIdentifier()] = new JObject
    {
        ["name"] = function.Name, ["page"] = function.Page?.Name, ["main"] = function.IsMainFunction,
    };

    internal static JObject Messages()
    {
        var rows = new JObject();
        try
        {
            // A11/A39: finish copying text before constructing another collection. Retain no native messages.
            var collection = new SysMessagesCollection();
            using SysMessagesEnumerator iterator = collection.GetSysMsgEnumerator();
            using var hash = SHA256.Create();
            while (iterator.MoveNext())
            {
                if (iterator.Current is BaseException message)
                {
                    string text = message.GetMessageText();
                    string index = message.GetMessageIndex();
                    int level = (int)message.MessageLevel;
                    // GetBookmarkID sets a bookmark; it is not a message identity. Hash copied fields instead.
                    string key = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(
                        index + "\n" + level.ToString(CultureInfo.InvariantCulture) + "\n" + text)));
                    int occurrences = (int?)rows[key]?["occurrences"] ?? 0;
                    rows[key] = new JObject { ["index"] = index, ["text"] = text,
                        ["level"] = level, ["occurrences"] = occurrences + message.NumberOfOccurrences };
                }
            }
        }
        catch (Exception ex)
        {
            rows["unavailable"] = ex.ToString();
        }

        return rows;
    }
}
