using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

internal sealed class PlacementDeleteReadback
{
    internal List<DrawingPlacement> Remaining { get; } = [];
    internal List<DrawingPlacement> Missing { get; } = [];
    internal List<DrawingPlacement> AlsoRemoved { get; } = [];
    internal List<(DrawingPlacement Row, string Change)> Changed { get; } = [];

    internal static PlacementDeleteReadback Compare(IReadOnlyList<DrawingPlacement> expected, IReadOnlyList<DrawingPlacement> actual,
        IReadOnlyList<DrawingPlacement> targets, bool hasCable)
    {
        var result = new PlacementDeleteReadback();
        var identities = new HashSet<(int, long)>(targets.Select(p => Identity(p.Locator)));
        bool IsTarget(DrawingPlacement p) => identities.Contains(Identity(p.Locator))
            || p.Parent is { } parent && identities.Contains(Identity(parent));
        result.Remaining.AddRange(actual.Where(IsTarget));
        bool ignoreConnections = targets.Any(p => PlacementDeleteOperation.IsFunction(p.Kind));
        var before = Roots(expected, ignoreConnections);
        var after = Roots(actual.Where(p => !IsTarget(p)), ignoreConnections);
        foreach (var key in before.Select(g => g.Key).Union(after.Select(g => g.Key)))
        {
            var unmatched = before[key].ToList();
            var candidates = after[key].ToList();
            foreach (Root root in after[key])
            {
                int same = unmatched.FindIndex(old => old.Row.Locator.Digest == root.Row.Locator.Digest
                    && old.Children.SequenceEqual(root.Children));
                if (same >= 0)
                {
                    unmatched.RemoveAt(same);
                    candidates.Remove(root);
                }
            }
            // Prefer the same root digest when only its children changed.
            foreach (Root root in candidates.ToArray())
            {
                int same = unmatched.FindIndex(old => old.Row.Locator.Digest == root.Row.Locator.Digest);
                if (same >= 0)
                {
                    result.Changed.Add((root.Row, "changed"));
                    unmatched.RemoveAt(same);
                    candidates.Remove(root);
                }
            }
            int paired = Math.Min(unmatched.Count, candidates.Count);
            result.Changed.AddRange(candidates.Select((root, i) => (root.Row, i < paired ? "changed" : "added")));
            // If a surplus is ambiguous, a disappearing cable point is a permitted explanation.
            IEnumerable<Root> surplus = hasCable && paired > 0 && paired < unmatched.Count
                ? unmatched.OrderBy(root => root.Row.Kind == "ConnectionDefinitionPoint").Skip(paired)
                : unmatched.Skip(paired);
            foreach (Root root in surplus)
            {
                (hasCable && root.Row.Kind == "ConnectionDefinitionPoint" ? result.AlsoRemoved : result.Missing).Add(root.Row);
            }
        }
        return result;
    }

    private sealed class Root(DrawingPlacement row, string[] children)
    {
        internal DrawingPlacement Row { get; } = row;
        internal string[] Children { get; } = children;
    }

    private static (int, long) Identity(DrawingLocator locator) => (locator.Type, locator.Oid);
    private static (int, long, string) ParentKey(DrawingLocator locator) => (locator.Type, locator.Oid, locator.Digest);

    private static ILookup<(int, long), Root> Roots(IEnumerable<DrawingPlacement> rows, bool ignoreConnections)
    {
        var visible = rows.Where(p => !ignoreConnections || p.Kind is not ("Connection" or "DynamicConnectionLine")).ToList();
        var children = visible.Where(p => p.Parent is not null).ToLookup(p => ParentKey(p.Parent!));
        return visible.Where(p => p.Parent is null).Select(p => new Root(p, Descendants(p))).ToLookup(r => Identity(r.Row.Locator));

        string[] Descendants(DrawingPlacement root)
        {
            var digests = new List<string>();
            var pending = new Stack<(int, long, string)>();
            var visited = new HashSet<(int, long, string)>();
            pending.Push(ParentKey(root.Locator));
            while (pending.Count > 0)
            {
                var parent = pending.Pop();
                if (visited.Add(parent))
                {
                    foreach (DrawingPlacement child in children[parent])
                    {
                        digests.Add(child.Locator.Digest);
                        pending.Push(ParentKey(child.Locator));
                    }
                }
            }
            digests.Sort(StringComparer.Ordinal);
            return digests.ToArray();
        }
    }

    internal void Verify()
    {
        int total = Remaining.Count + Missing.Count;
        if (total == 0)
        {
            return;
        }
        var entries = Remaining.Select(p => Detail("remaining", p)).Concat(Missing.Select(p => Detail("missing", p))).Take(10);
        string suffix = total > 10 ? $" and {total - 10} more" : string.Empty;
        throw new BridgeErrorException(ErrorReason.EplanOperationFailed,
            "Deletion changed objects outside the plan or left targets behind. " + string.Join("; ", entries) + suffix);
    }

    private static string Detail(string label, DrawingPlacement row)
    {
        row.Properties.TryGetValue("name", out string? name);
        return $"{label}: {row.Kind} {row.Locator.Type}/{row.Locator.Oid}" + (string.IsNullOrEmpty(name) ? string.Empty : " " + name);
    }

    internal void AddTo(JObject result, string? warning)
    {
        if (AlsoRemoved.Count > 0)
        {
            result["alsoRemoved"] = new JArray(AlsoRemoved.Select(Describe));
        }
        if (Changed.Count > 0)
        {
            result["changed"] = new JArray(Changed.Take(20).Select(entry =>
            {
                JObject row = Describe(entry.Row);
                row["change"] = entry.Change;
                return row;
            }));
            if (Changed.Count > 20)
            {
                result["changedTotal"] = Changed.Count;
            }
            warning = (string.IsNullOrEmpty(warning) ? string.Empty : warning + " ")
                + $"EPLAN also updated {Changed.Count} other object(s) on the page; see changed.";
        }
        result["warning"] = warning;
    }

    private static JObject Describe(DrawingPlacement row)
    {
        row.Properties.TryGetValue("name", out string? name);
        return new JObject { ["type"] = row.Locator.Type, ["oid"] = row.Locator.Oid, ["kind"] = row.Kind, ["name"] = name is null ? JValue.CreateNull() : new JValue(name) };
    }
}
