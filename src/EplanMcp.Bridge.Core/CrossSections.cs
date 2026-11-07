using System.Globalization;

namespace EplanMcp.Bridge.Core;

internal static class CrossSections
{
    internal static string Format(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture).Replace('.', ',');
    }
}
