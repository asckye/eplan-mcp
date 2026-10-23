using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Plans;

/// <summary>Exact ordinal sets. Empty clauses impose no filter; an entirely empty resolved scope selects nothing.</summary>
internal static partial class PlanScopeRules
{
    public static PlanScope Pages(IEnumerable<string> pages) => new([.. pages.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)], [], []);

    public static PlanScope Whole(PlanSnapshot snapshot) => Pages(snapshot.Pages.Select(p => p.Name));

    public static PlanScope Resolve(PlanSnapshot snapshot, PlanScope scope) => new(
        [.. scope.Pages.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
        [.. scope.Structures.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
        [.. scope.Locators.Select(l => snapshot.Pages.SelectMany(p => p.Objects).Select(o => Locator(snapshot.Project.Path, o))
            .First(a => Same(l, a))).Distinct().OrderBy(l => l.Kind, StringComparer.Ordinal).ThenBy(l => l.Type).ThenBy(l => l.Oid)]);

    public static PlanLocator Locator(string project, SnapshotObject obj) => new(PlanSnapshotHashes.ProjectKey(project),
        obj.Kind, obj.Type, obj.Oid, obj.Name, obj.Page, obj.Category, obj.Pos);

    public static bool StructurePrefix(string page, string prefix)
    {
        string structure = page.Split('/')[0];
        string[] actual = Segments().Split(structure), wanted = Segments().Split(prefix);
        return prefix.Length > 0 && wanted.Length <= actual.Length
            && actual.Take(wanted.Length).SequenceEqual(wanted, StringComparer.Ordinal);
    }

    public static bool Contains(PlanScope scope, PlanLocator locator) =>
        (scope.Pages.Length + scope.Structures.Length + scope.Locators.Length > 0)
        && (scope.Pages.IsEmpty || scope.Pages.Contains(locator.Kind == "page" ? locator.Name! : locator.Page!, StringComparer.Ordinal))
        && (scope.Structures.IsEmpty || scope.Structures.Any(s => StructurePrefix(locator.Kind == "page" ? locator.Name! : locator.Page ?? string.Empty, s)))
        && (scope.Locators.IsEmpty || scope.Locators.Any(l => Same(l, locator)));

    public static ImmutableArray<SnapshotObject> Select(PlanSnapshot snapshot, PlanScope scope) =>
        [.. snapshot.Pages.SelectMany(p => p.Objects).Where(o => Contains(scope, Locator(snapshot.Project.Path, o)))
            .OrderBy(o => o.Kind, StringComparer.Ordinal).ThenBy(o => o.Type).ThenBy(o => o.Oid)];

    public static bool Same(PlanLocator expected, PlanLocator actual) =>
        Path.IsPathFullyQualified(expected.Project) && Path.IsPathFullyQualified(actual.Project)
        && PlanSnapshotHashes.ProjectKey(expected.Project) == PlanSnapshotHashes.ProjectKey(actual.Project)
        && expected.Kind == actual.Kind && expected.Type == actual.Type && expected.Oid == actual.Oid
        && (expected.Name is null || expected.Name == actual.Name) && (expected.Page is null || expected.Page == actual.Page)
        && (expected.Category is null || expected.Category == actual.Category)
        && (expected.Pos is null || Near(expected.Pos, actual.Pos));

    public static ImmutableArray<string> Missing(PlanBaseline baseline, PlanScope scope)
    {
        PlanSnapshot snapshot = baseline.Snapshot;
        var all = snapshot.Pages.SelectMany(p => p.Objects).Select(o => Locator(snapshot.Project.Path, o)).ToArray();
        return [.. scope.Pages.Except(snapshot.Pages.Select(p => p.Name), StringComparer.Ordinal).Select(_ => "snapshot:pages")
            .Concat(scope.Locators.Where(l => !all.Any(a => Same(l, a))).Select(_ => "snapshot:locators"))
            .Concat(scope.Structures.Where(s => s.Length < 2 || !"=+&#".Contains(s[0], StringComparison.Ordinal) || s.IndexOfAny(['/', '*', '?']) >= 0
                || s.Any(char.IsControl) || Segments().Split(s).Skip(1).Any(segment => segment.Length < 2))
                .Select(_ => "scope:structure_segments"))
            .Concat(baseline.Names.Where(n => n.Kind == "page" && !scope.Structures.IsEmpty
                && (scope.Pages.IsEmpty || scope.Pages.Contains(n.Name, StringComparer.Ordinal)) && scope.Structures.Any(s => StructurePrefix(n.Name, s))
                && !snapshot.Pages.Any(p => p.Name == n.Name)).Select(_ => "snapshot:pages"))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    private static bool Near(string expected, string? actual)
    {
        string[] left = expected.Split(','), right = actual?.Split(',') ?? [];
        return left.Length == 2 && right.Length == 2 && left.Zip(right).All(p =>
            double.TryParse(p.First, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) && double.IsFinite(x)
            && double.TryParse(p.Second, NumberStyles.Float, CultureInfo.InvariantCulture, out double y) && double.IsFinite(y)
            && Math.Abs(x - y) <= 0.001 + 1e-9);
    }

    [GeneratedRegex(@"(?=[=+&#])", RegexOptions.CultureInvariant)]
    private static partial Regex Segments();
}
