using System.Globalization;

namespace EplanMcp.Host.Bridge;

/// <summary>Where an EPLAN version stands in the support range of ADR-0009.</summary>
internal enum EplanSupport
{
    /// <summary>Tested with EplanMcp: 2026 (M1, M2).</summary>
    Verified,

    /// <summary>In the range, not tested yet; spike S7 tests these versions one by one.</summary>
    Expected,

    /// <summary>Outside the range, 2022 and 2023 included, or a version that cannot be read.</summary>
    Unsupported,
}

/// <summary>
/// The EPLAN versions EplanMcp supports (ADR-0009): one net481 add-in for 2024-2027, each listed as verified once
/// spike S7 has tested it. Doctor and eplan_session_status say where the EPLAN at hand stands.
/// </summary>
internal static class EplanVersions
{
    public const int First = 2024;
    public const int Last = 2027;

    /// <summary>The range as the texts quote it.</summary>
    public static readonly string Range = string.Create(CultureInfo.InvariantCulture, $"{First}-{Last}");

    // Add a version here once spike S7 has run the Large tests on it.
    private static readonly int[] _verified = [2026];

    /// <summary>By the year EPLAN's file version starts with, for example <c>2026</c> in <c>2026.0.3.25421</c>.</summary>
    public static EplanSupport Support(string? version) =>
        int.TryParse(version?.Split('.')[0], NumberStyles.None, CultureInfo.InvariantCulture, out int year)
            ? _verified.Contains(year) ? EplanSupport.Verified
            : year is >= First and <= Last ? EplanSupport.Expected
            : EplanSupport.Unsupported
            : EplanSupport.Unsupported;
}
