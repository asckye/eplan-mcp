using System.Runtime.CompilerServices;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel.Graphics;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// The outline of a property text. <c>PropertyPlacement.GetBoundingBox(Boolean)</c> exists from EPLAN 2026; the 2025
/// API documents only <c>GetBoundingBox()</c> (eplan.help API pages for 2025 and 2026, read 2026-11-19; found by a
/// build against the 2025.0.3 assemblies reported in asckye/eplan-mcp#664).
/// </summary>
internal static class EplanTextBounds
{
    private static bool _absoluteOverloadMissing;

    /// <summary>
    /// The absolute outline where the platform has the overload (A87). Without it the outline is what the parameterless
    /// overload returns, which on 2026 is relative to the owner; on 2025 that is not verified.
    /// </summary>
    internal static PointD[] Of(PropertyPlacement placement)
    {
        if (!_absoluteOverloadMissing)
        {
            try
            {
                return Absolute(placement);
            }
            catch (MissingMethodException)
            {
                // Thrown when the runtime compiles Absolute, before any of it runs; later calls skip the attempt.
                _absoluteOverloadMissing = true;
            }
        }

        return placement.GetBoundingBox();
    }

    /// <summary>Kept out of line so that a platform without the overload fails here and not in the caller.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static PointD[] Absolute(PropertyPlacement placement) => placement.GetBoundingBox(false);
}
