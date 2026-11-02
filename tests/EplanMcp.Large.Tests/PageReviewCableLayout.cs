using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>A64: the deliberately wrong segment must be shared; symbol pin directions alone do not establish the trunk.</summary>
internal static class PageReviewCableLayout
{
    internal static (double[][] Trunk, double[][] Branch) Read(JsonElement observation, string from, string to, double grid)
    {
        JsonElement[] connections = observation.GetProperty("connections").EnumerateArray().ToArray();
        JsonElement target = Assert.Single(connections, c =>
            c.GetProperty("from").GetProperty("name").GetString() == from && c.GetProperty("to").GetProperty("name").GetString() == to
            || c.GetProperty("from").GetProperty("name").GetString() == to && c.GetProperty("to").GetProperty("name").GetString() == from);
        JsonElement[] segments = observation.TryGetProperty("segments", out JsonElement found) && found.ValueKind == JsonValueKind.Array
            ? found.EnumerateArray().ToArray() : [];
        double[][][] Segments(JsonElement row, bool required = false)
        {
            string Diagnostic(string reason) => $"Connection {row.GetProperty("from").GetProperty("name").GetString()} -> {row.GetProperty("to").GetProperty("name").GetString()} ({row.GetRawText()}) has {reason}. Segments found: [{string.Join(",", segments.Select(s => s.GetRawText()))}]";
            if (row.TryGetProperty("drawnPath", out JsonElement drawnPath) && drawnPath.ValueKind == JsonValueKind.Array)
            {
                double[][] path = drawnPath.EnumerateArray().Select(p => p.EnumerateArray().Select(v => v.GetDouble()).ToArray()).ToArray();
                Assert.True(!required || path.Length > 1, Diagnostic("an empty drawn path"));
                return path.Zip(path.Skip(1), (a, b) => new[] { a, b }).ToArray();
            }
            if (!row.GetProperty("from").TryGetProperty("oid", out JsonElement fromOid)
                || !row.GetProperty("to").TryGetProperty("oid", out JsonElement toOid))
            {
                Assert.False(required, Diagnostic("no endpoint object ids for a segment chain"));
                return [];
            }
            long destination = toOid.GetInt64();
            var visited = new HashSet<long> { fromOid.GetInt64() };
            var chain = new List<double[][]>();
            var paths = new List<double[][][]>();
            void Find(long current)
            {
                if (paths.Count > 1)
                { return; }
                if (current == destination)
                {
                    paths.Add(chain.ToArray());
                    return;
                }
                foreach (JsonElement segment in segments)
                {
                    long start = segment.GetProperty("startOid").GetInt64(), end = segment.GetProperty("endOid").GetInt64();
                    if (start != current && end != current)
                    { continue; }
                    long next = start == current ? end : start;
                    if (!visited.Add(next))
                    { continue; }
                    double[] a = segment.GetProperty("start").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                    double[] b = segment.GetProperty("end").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                    // A node's pins can have different positions; only its object id joins the segments.
                    chain.Add(start == current ? [a, b] : [b, a]);
                    Find(next);
                    chain.RemoveAt(chain.Count - 1);
                    visited.Remove(next);
                }
            }
            Find(fromOid.GetInt64());
            Assert.True(paths.Count <= 1, Diagnostic("an ambiguous segment chain"));
            Assert.True(!required || paths.Count == 1 && paths[0].Length > 0, Diagnostic("no segment chain"));
            return paths.Count == 1 ? paths[0] : [];
        }
        static bool Same(double[][] a, double[][] b) => a[0].SequenceEqual(b[0]) && a[1].SequenceEqual(b[1]) || a[0].SequenceEqual(b[1]) && a[1].SequenceEqual(b[0]);
        double[][][] targetSegments = Segments(target, required: true), all = connections.SelectMany(c => Segments(c)).ToArray();
        double[][] trunk = Assert.Single(targetSegments, s => all.Count(other => Same(s, other)) > 1);
        double[][] branch = Assert.Single(targetSegments, s => all.Count(other => Same(s, other)) == 1);
        double[][] Cross(double[][] segment)
        {
            Assert.Equal(segment[0][0], segment[1][0]);
            double x = segment[0][0], y = Math.Round((segment[0][1] + segment[1][1]) / (2 * grid)) * grid;
            Assert.InRange(y, Math.Min(segment[0][1], segment[1][1]) + grid, Math.Max(segment[0][1], segment[1][1]) - grid);
            return [[x - 10 * grid, y], [x + 10 * grid, y]];
        }
        return (Cross(trunk), Cross(branch));
    }
}
