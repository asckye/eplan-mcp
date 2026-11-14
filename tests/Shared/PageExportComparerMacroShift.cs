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

    /// <summary>
    /// Fields the substitute macro redraws in its own convention: EPLAN stores an arc's extent in angles whose start
    /// and end a macro insertion does not preserve (api-notes: the sample's circles keep start = end). The owner
    /// decided on 2026-11-13 that these count as macro version differences too, so an object already pinned to a
    /// macro's translation may differ in them; every entry names the ones that do.
    /// </summary>
    private static readonly string[] _redrawnByTheMacro = ["startAngle", "endAngle"];

    /// <summary>A page can hold several substituted macros, so each qualifying translation is taken in turn.</summary>
    internal static void Separate(PageExportComparison result)
    {
        var translations = new List<(double X, double Y)>();
        while (SeparateOne(result, translations))
        {
            // Every round pairs at least MinimumObjects leftovers, so the leftovers run out.
        }
        // Only now, with each macro's translation measured, are leftovers at one of them taken in its own convention.
        foreach ((double X, double Y) translation in translations)
        { SeparateRedrawn(result, translation); }
    }

    private static bool SeparateOne(PageExportComparison result, List<(double X, double Y)> translations)
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
        if (candidates.Where(candidate => candidate.Delta is not (0, 0)).GroupBy(candidate => candidate.Delta)
            .OrderByDescending(group => group.Count()).FirstOrDefault() is not { } shift || shift.Count() < MinimumObjects)
        { return false; }

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
            result.MacroShiftDifferences.Add(new(type, source, target, shift.Key.X, shift.Key.Y, []));
        }
        if (usedSources.Count < MinimumObjects)
        { return false; }
        translations.Add(shift.Key);
        return true;
    }

    /// <summary>Leftovers exactly at <paramref name="translation"/> whose only remaining differences are the macro's own convention.</summary>
    private static void SeparateRedrawn(PageExportComparison result, (double X, double Y) translation)
    {
        foreach (string type in _types)
        {
            if (!result.Types.TryGetValue(type, out PageExportTypeComparison? group))
            { continue; }
            foreach (JObject missing in group.Missing.ToArray())
            {
                JObject? paired = group.Extra.FirstOrDefault(extra => Delta(missing, extra) == translation
                    && Redrawn(missing, extra) is { Length: > 0 });
                if (paired is null)
                { continue; }
                group.Missing.Remove(missing);
                group.Extra.Remove(paired);
                result.MacroShiftDifferences.Add(new(type, missing, paired, translation.X, translation.Y,
                    Redrawn(missing, paired)!));
            }
        }
    }

    /// <summary>The convention fields that differ, or null when something outside them does.</summary>
    private static string[]? Redrawn(JObject left, JObject right)
    {
        if (Shape(left) is not JObject one || Shape(right) is not JObject other)
        { return null; }
        string[] differing = [.. _redrawnByTheMacro.Where(field => !JToken.DeepEquals(one[field], other[field]))];
        foreach (string field in _redrawnByTheMacro)
        { one.Remove(field); other.Remove(field); }
        return JToken.DeepEquals(one, other) ? differing : null;
    }

    /// <summary>The translation from <paramref name="source"/> to <paramref name="target"/>, rounded to the micron.</summary>
    private static (double X, double Y)? Delta(JObject source, JObject target)
    {
        if (Point(source["location"]) is not { } from || Point(target["location"]) is not { } to)
        { return null; }
        return (Math.Round(to.X - from.X, 6), Math.Round(to.Y - from.Y, 6));
    }

    /// <summary>
    /// The same drawing, wherever it sits: every geometry field measured from the object's own location, so a shared
    /// translation cancels out. The bounding box is left out because it grows with the pen width, and the pen, layer
    /// and properties are left out because the substitute macro owns them; each entry records that offset and keeps
    /// those fields in its copy of both objects.
    /// </summary>
    private static bool SameShape(JObject left, JObject right) => JToken.DeepEquals(Shape(left), Shape(right));

    private static readonly string[] _ownedByTheMacro = ["oid", "errors", "properties", "boundingBox", "location", "layer", "pen"];

    private static JToken Shape(JObject value)
    {
        if (Point(value["location"]) is not { } origin)
        { return JValue.CreateNull(); }
        var shape = (JObject)value.DeepClone();
        foreach (string field in _ownedByTheMacro)
        { shape.Remove(field); }
        Relative(shape, origin);
        return shape;
    }

    /// <summary>Rewrites every {x, y} in place as an offset from <paramref name="origin"/>, rounded to the micron.</summary>
    private static void Relative(JToken token, (double X, double Y) origin)
    {
        if (token is JObject point && Point(point) is { } absolute)
        {
            point["x"] = Math.Round(absolute.X - origin.X, 6);
            point["y"] = Math.Round(absolute.Y - origin.Y, 6);
            return;
        }
        foreach (JToken child in token is JObject nested ? nested.PropertyValues() : token.Children())
        { Relative(child, origin); }
    }

    private static (double X, double Y)? Point(JToken? value) => value is JObject point
        && Coordinate(point["x"]) is { } x && Coordinate(point["y"]) is { } y ? (x, y) : null;

    private static double? Coordinate(JToken? value) => value?.Type is JTokenType.Integer or JTokenType.Float ? (double)value
        : double.TryParse((string?)value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : null;
}

/// <summary>
/// One object a substitute macro drew at a constant offset from the sample's. <paramref name="Redrawn"/> names the
/// fields the macro also states in its own convention; it is empty when only the position differs.
/// </summary>
internal sealed record PageExportMacroShift(string Type, JObject Source, JObject Target, double X, double Y,
    IReadOnlyList<string> Redrawn);
