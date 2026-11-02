using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A64/A75: translate a cable from a shared trunk onto a parallel exclusive branch using observed geometry.</summary>
internal static class PageReviewCableFix
{
    internal static JObject Arguments(double grid, JToken? cable, JToken? target, JArray connections)
    {
        if (!Finite(grid) || grid <= 0 || cable?["locator"] is not JObject locator
            || Point(cable["position"]) is not { } location || Path(cable["points"]) is not { Length: 2 } line
            || target is null || Path(target["drawnPath"]) is not { } path)
        { return new(); }
        var others = new List<double[][]>();
        foreach (JToken connection in connections.Where(c => !ReferenceEquals(c, target)))
        {
            // An unreadable route cannot establish that a branch is exclusive.
            if (Path(connection["drawnPath"]) is not { } other)
            { return new(); }
            others.Add(other);
        }
        double[][][] segments = Segments(path).ToArray(), otherSegments = others.SelectMany(Segments).ToArray();
        bool Shared(double[][] segment) => otherSegments.Any(other => Same(segment, other));
        var moves = new List<double[]>();
        foreach (double[][] trunk in segments.Where(s => Shared(s) && Crosses(line, s)))
        {
            int axis = trunk[0][0] == trunk[1][0] ? 1 : 0;
            foreach (double[][] branch in segments.Where(s => !Shared(s) && (s[0][1 - axis] == s[1][1 - axis])))
            {
                double low = Math.Ceiling((Math.Min(branch[0][axis], branch[1][axis]) + grid) / grid);
                double high = Math.Floor((Math.Max(branch[0][axis], branch[1][axis]) - grid) / grid);
                if (low > high)
                { continue; }
                double middle = Math.Round((branch[0][axis] + branch[1][axis]) / (2 * grid));
                double delta = Math.Max(low, Math.Min(high, middle)) * grid - line[0][axis];
                double[][] movedLine = line.Select(p => p.Select((v, i) => i == axis ? v + delta : v).ToArray()).ToArray();
                if (!Crosses(movedLine, branch) || segments.Count(s => Crosses(movedLine, s)) != 1
                    || otherSegments.Any(s => Crosses(movedLine, s)))
                { continue; }
                double[] destination = [.. location];
                destination[axis] += delta;
                if (destination.All(Finite) && destination.All(n => n is >= 0 and <= 10000))
                { moves.Add(destination); }
            }
        }
        double[][] distinct = moves.GroupBy(p => new JArray(p).ToString(Newtonsoft.Json.Formatting.None), StringComparer.Ordinal).Select(g => g.First()).ToArray();
        if (distinct.Length != 1)
        { return new(); }
        // The line is already snapped along the branch; snapping its insertion point again would change that crossing.
        return new() { ["snap_to_grid"] = false, ["moves"] = new JArray(new JObject { ["locator"] = locator.DeepClone(), ["x_mm"] = distinct[0][0], ["y_mm"] = distinct[0][1] }) };
    }

    private static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
    private static double[]? Point(JToken? token) => token is JArray { Count: 2 } a && a.All(n => n.Type is JTokenType.Integer or JTokenType.Float && Finite((double)n)) ? a.Values<double>().ToArray() : null;
    private static double[][]? Path(JToken? token)
    {
        if (token is not JArray { Count: >= 2 } points)
        { return null; }
        var result = new List<double[]>();
        foreach (JToken point in points)
        {
            if (Point(point) is not { } p)
            { return null; }
            result.Add(p);
        }
        return Segments(result.ToArray()).All(s => (s[0][0] == s[1][0]) != (s[0][1] == s[1][1])) ? result.ToArray() : null;
    }
    private static IEnumerable<double[][]> Segments(double[][] path) => path.Zip(path.Skip(1), (a, b) => new[] { a, b });
    private static bool Same(double[][] a, double[][] b) => a[0].SequenceEqual(b[0]) && a[1].SequenceEqual(b[1]) || a[0].SequenceEqual(b[1]) && a[1].SequenceEqual(b[0]);
    private static bool Crosses(double[][] line, double[][] segment)
    {
        bool vertical = segment[0][0] == segment[1][0];
        int axis = vertical ? 1 : 0;
        return (line[0][axis] == line[1][axis]) && line[0][1 - axis] != line[1][1 - axis]
            && segment[0][1 - axis] > Math.Min(line[0][1 - axis], line[1][1 - axis])
            && segment[0][1 - axis] < Math.Max(line[0][1 - axis], line[1][1 - axis])
            && line[0][axis] > Math.Min(segment[0][axis], segment[1][axis])
            && line[0][axis] < Math.Max(segment[0][axis], segment[1][axis]);
    }
}
