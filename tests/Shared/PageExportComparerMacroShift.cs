using System.Globalization;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Tests;

/// <summary>
/// A substitute library macro draws the same graphics as the sample's macro, but places them at a slightly different
/// offset from its own devices: the devices are inserted where the sample has them, so every graphic of that macro
/// lands shifted by one constant translation. The owner's rule of 2026-11-04 lists such a substitution as a macro
/// version difference rather than something to hand-draw, so these objects are paired, reported with the measured
/// offset and kept out of the matching rate instead of counting once as missing and once as extra.
/// Only a translation many objects share to the micron qualifies; a shift one object has on its own stays a
/// difference, and so does any object whose shape changed as well.
/// </summary>
internal static class PageExportComparerMacroShift
{
    /// <summary>Graphics a macro owns. Device and text placements keep their own strict comparison.</summary>
    private static readonly string[] _types = ["Arc", "Line", "PolyLine", "Rectangle"];

    /// <summary>Below this, a shared translation is coincidence rather than one macro's geometry.</summary>
    internal const int MinimumObjects = 10;

    internal static void Separate(PageExportComparison result)
    {
        var candidates = new List<(string Type, JObject Source, JObject Target, (double X, double Y) Delta)>();
        foreach (string type in _types)
        {
            if (!result.Types.TryGetValue(type, out PageExportTypeComparison? group))
            { continue; }
            foreach (JObject missing in group.Missing)
            {
                foreach (JObject extra in group.Extra.Where(extra => SameShape(missing, extra)))
                {
                    if (Delta(missing, extra) is { } delta)
                    { candidates.Add((type, missing, extra, delta)); }
                }
            }
        }
        if (candidates.GroupBy(candidate => candidate.Delta).OrderByDescending(group => group.Count())
            .FirstOrDefault() is not { } shift || shift.Count() < MinimumObjects || shift.Key is (0, 0))
        { return; }

        var usedSources = new HashSet<JObject>();
        var usedTargets = new HashSet<JObject>();
        foreach ((string type, JObject source, JObject target, _) in shift)
        {
            if (!usedSources.Add(source))
            { continue; }
            if (!usedTargets.Add(target))
            { usedSources.Remove(source); continue; }
            PageExportTypeComparison group = result.Types[type];
            group.Missing.Remove(source);
            group.Extra.Remove(target);
            result.MacroShiftDifferences.Add(new(type, source, target, shift.Key.X, shift.Key.Y));
        }
    }

    /// <summary>The translation from <paramref name="source"/> to <paramref name="target"/>, rounded to the micron.</summary>
    private static (double X, double Y)? Delta(JObject source, JObject target)
    {
        if (Point(source["location"]) is not { } from || Point(target["location"]) is not { } to)
        { return null; }
        return (Math.Round(to.X - from.X, 6), Math.Round(to.Y - from.Y, 6));
    }

    /// <summary>The same drawing, wherever it sits: the bounding box's extent, and a circle's angles and radius.</summary>
    private static bool SameShape(JObject left, JObject right) => Extent(left) is { } size && Extent(right) is { } other
        && Math.Abs(size.Width - other.Width) <= PageExportComparer.PositionTolerance
        && Math.Abs(size.Height - other.Height) <= PageExportComparer.PositionTolerance
        && JToken.DeepEquals(left["startAngle"], right["startAngle"]) && JToken.DeepEquals(left["endAngle"], right["endAngle"])
        && JToken.DeepEquals(left["radius"], right["radius"]);

    private static (double Width, double Height)? Extent(JObject value) =>
        value["boundingBox"] is JArray { Count: 2 } box && Point(box[0]) is { } first && Point(box[1]) is { } second
            ? (Math.Abs(second.X - first.X), Math.Abs(second.Y - first.Y)) : null;

    private static (double X, double Y)? Point(JToken? value) => value is JObject point
        && Coordinate(point["x"]) is { } x && Coordinate(point["y"]) is { } y ? (x, y) : null;

    private static double? Coordinate(JToken? value) => value?.Type is JTokenType.Integer or JTokenType.Float ? (double)value
        : double.TryParse((string?)value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : null;
}

/// <summary>One object a substitute macro drew at a constant offset from the sample's.</summary>
internal sealed record PageExportMacroShift(string Type, JObject Source, JObject Target, double X, double Y);
