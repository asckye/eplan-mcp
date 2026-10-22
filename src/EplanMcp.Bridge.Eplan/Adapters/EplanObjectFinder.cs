using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Finds objects for object.resolve by enumerating and comparing <c>ObjectIdentifier</c>, which S28 verified, rather than
/// <c>StorableObject.FromStringIdentifier</c>, which no spike tried (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.6):
/// pages from <c>GetPages(null)</c>, functions on the locator's page and then on every page (<c>Page.AllPlacements</c>,
/// as the snapshot reads them), connections from <c>GetConnections(null)</c>; S28 enumerated the sample project in about
/// a second. Each object is read by <see cref="EplanSnapshotReader"/>'s own code, so its hash is the snapshot's. Main
/// thread, inside the dispatcher's LockingStep.
/// </summary>
internal sealed class EplanObjectFinder : IObjectFinder
{
    public IReadOnlyList<SnapshotSourceObject?> Find(string projectPath, IReadOnlyList<ObjectLocator> locators, string profile)
    {
        MainThread.Assert();
        IEnumerable<SnapshotProperty> registered = SnapshotCanonical.Registered(profile)!.Values;
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        Page[] pages = finder.GetPages(null);
        Connection[]? connections = null;
        ILookup<long, (Function Function, Page Page)>? functions = null; // every page's, built once when a function is not on its page
        var results = new List<SnapshotSourceObject?>();
        foreach (ObjectLocator locator in locators)
        {
            switch (locator.Kind)
            {
                case "page":
                    results.Add(Pick(pages.Where(p => p.ObjectIdentifier == locator.Oid), locator) is { } page ? EplanSnapshotReader.PageObject(page, registered) : null);
                    break;
                case "function":
                    Page? recorded = locator.Page is null ? null : pages.FirstOrDefault(p => string.Equals(p.Name, locator.Page, StringComparison.Ordinal));
                    (Function Function, Page Page)? found = Function(recorded is null ? [] : Placed([recorded])[locator.Oid], locator, typedOnly: true);
                    if (found is null)
                    {
                        functions ??= Placed(pages);
                        found = Function(functions[locator.Oid], locator, typedOnly: false);
                    }

                    results.Add(found is { } f ? EplanSnapshotReader.FunctionObject(f.Function, f.Page.Name, registered) : null);
                    break;
                default:
                    connections ??= finder.GetConnections(null);
                    Connection? connection = Pick(connections.Where(c => c.ObjectIdentifier == locator.Oid), locator);
                    results.Add(connection is null ? null : EplanSnapshotReader.ConnectionObject(connection, EplanConnectionReader.PageOf(connection)?.Name, registered));
                    break;
            }
        }

        return results;
    }

    /// <summary>The functions placed on <paramref name="pages"/> by their number.</summary>
    private static ILookup<long, (Function Function, Page Page)> Placed(IEnumerable<Page> pages) =>
        pages.SelectMany(p => p.AllPlacements.OfType<Function>().Select(f => (f, p))).ToLookup(m => m.f.ObjectIdentifier, m => (m.f, m.p));

    /// <summary>Of <paramref name="matches"/>, the function of the locator's type; unless <paramref name="typedOnly"/>, one of another type when there is none.</summary>
    private static (Function Function, Page Page)? Function(IEnumerable<(Function Function, Page Page)> matches, ObjectLocator locator, bool typedOnly)
    {
        (Function Function, Page Page)[] all = [.. matches];
        foreach ((Function Function, Page Page) match in all.Where(m => TypeOf(m.Function) == locator.Type))
        {
            return match;
        }

        return typedOnly || all.Length == 0 ? null : all[0];
    }

    /// <summary>Of the objects with the locator's number, the one of its type; any of them otherwise, which the check then finds of another type.</summary>
    private static T? Pick<T>(IEnumerable<T> candidates, ObjectLocator locator)
        where T : StorableObject
    {
        T[] all = [.. candidates];
        return all.FirstOrDefault(o => TypeOf(o) == locator.Type) ?? all.FirstOrDefault();
    }

    private static int TypeOf(StorableObject o) => SnapshotCanonical.TypeOf(o.ToStringIdentifier());
}
