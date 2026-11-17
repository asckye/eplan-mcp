using System.Globalization;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>A66: preserve layer inheritance before converting native text metadata.</summary>
internal static class EplanTextFormat
{
    internal static string Angle(double radians) => radians == -16002 || !Finite(radians) ? "from_layer"
        : Math.Round((radians * 180 / Math.PI % 360 + 360) % 360, 8).ToString("R", CultureInfo.InvariantCulture);

    internal static string Height(double height) => height <= 0 || !Finite(height) ? "from_layer"
        : DrawingCoordinates.ApiToMillimetres(height).ToString("R", CultureInfo.InvariantCulture);

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
