namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Two-dimensional Circuit coordinates: millimetres, lower-left origin, y upwards (api-notes A3).</summary>
public static class DrawingCoordinates
{
    /// <summary>Converts a finite signed millimetre value to API units; offsets may be negative.</summary>
    public static double MillimetresToApi(double value) => Finite(value, "/coordinate");
    /// <summary>Converts a finite signed API value to millimetres (1:1, A3).</summary>
    public static double ApiToMillimetres(double value) => Finite(value, "/coordinate");
    /// <summary>Snaps a 0..10,000 mm coordinate to the page grid; ties go away from zero. Disabled means unchanged.</summary>
    public static double Snap(double value, double gridSize, bool enabled = true)
    {
        Finite(value, "/coordinate");
        Finite(gridSize, "/gridSize");
        if (gridSize <= 0 || value is < 0 or > 10000)
        {
            throw WriteArguments.Invalid(["/coordinate must be 0..10000 mm and /gridSize must be positive."]);
        }

        double actual = enabled ? Math.Round(value / gridSize, MidpointRounding.AwayFromZero) * gridSize : value;
        return Finite(actual, "/coordinate") is >= 0 and <= 10000 ? actual
            : throw WriteArguments.Invalid(["/coordinate snapped outside 0..10000 mm."]);
    }

    /// <summary>Checks every full absolute outline, including following property text. Missing/invalid bounds fail closed.</summary>
    public static bool Contains(double[]? pageSize, IEnumerable<double[]?> outlines)
    {
        double[]?[] all = outlines.ToArray();
        return pageSize is { Length: 2 } && pageSize.All(v => IsFinite(v) && v > 0)
            && all.Length > 0 && all.All(b => ValidBounds(b) && b![0] >= 0 && b[1] >= 0 && b[2] <= pageSize[0] && b[3] <= pageSize[1]);
    }

    /// <summary>Whether an absolute min-x/min-y/max-x/max-y rectangle is finite and ordered.</summary>
    public static bool ValidBounds(double[]? bounds) => bounds is { Length: 4 } && bounds.All(IsFinite) && bounds[0] <= bounds[2] && bounds[1] <= bounds[3];

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static double Finite(double value, string at) => IsFinite(value) ? value
        : throw WriteArguments.Invalid([at + " must be finite."]);
}
