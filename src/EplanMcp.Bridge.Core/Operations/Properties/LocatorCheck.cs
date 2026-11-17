using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Checks a locator against the object found for it (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.6): a number can
/// be given again after a delete (EPLAN docs, api-notes A32), so a locator whose object does not match its description
/// is stale, and nothing else is looked for in its place. Pure logic.
/// </summary>
public static class LocatorCheck
{
    /// <summary>The position tolerance, millimetres: the rounding of the snapshot (A36).</summary>
    public const double Tolerance = 0.001;

    /// <summary>The kinds a locator may have.</summary>
    public static readonly IReadOnlyList<string> Kinds = ["page", "function", "connection"];

    /// <summary>
    /// The result of locator <paramref name="index"/>: <paramref name="found"/> is the object the finder returned for
    /// it (normalised and hashed), or null; <paramref name="projectPath"/> the project it was looked for in.
    /// </summary>
    public static LocatorResult Check(int index, ObjectLocator locator, string projectPath, SnapshotObject? found)
    {
        var result = new LocatorResult { Index = index, Status = LocatorResult.Stale };
        if (!ProjectResolver.SamePath(projectPath, locator.Project))
        {
            result.Reason = "project";
            return result;
        }

        if (found is null)
        {
            result.Reason = "not_found";
            return result;
        }

        (result.ActualName, result.ActualPage, result.ActualCategory, result.ActualPos) = (found.Name, found.Page, found.Category, found.Pos);
        result.Reason = found.Type != locator.Type ? "type"
            : Differs(locator.Name, found.Name) ? "name"
            : Differs(locator.Page, found.Page) ? "page"
            : Differs(locator.Category, found.Category) ? "category"
            : locator.Pos is { } pos && !Near(pos, found.Pos) ? "position"
            : null;
        if (result.Reason is null)
        {
            result.Status = LocatorResult.Resolved;
            result.Hash = found.Hash;
        }

        return result;
    }

    /// <summary>A position <c>x,y</c> in millimetres, culture-invariant; null when it is not one.</summary>
    public static (double X, double Y)? Position(string? pos)
    {
        string[] parts = pos?.Split(',') ?? [];
        return parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
            ? (x, y)
            : null;
    }

    private static bool Differs(string? expected, string? actual) =>
        expected is not null && !string.Equals(SnapshotValues.Text(expected), actual, StringComparison.Ordinal);

    private static bool Near(string expected, string? actual) =>
        Position(expected) is { } e && Position(actual) is { } a
        && Math.Abs(e.X - a.X) <= Tolerance + 1e-9 && Math.Abs(e.Y - a.Y) <= Tolerance + 1e-9;
}
