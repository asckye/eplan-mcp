using System.IO;

namespace EplanMcp.Workbench.Mcp;

/// <summary>One EPLAN year as the MCP page's version tiles show it: installed or not, and which variants.</summary>
internal sealed record EplanInstall(int Year, IReadOnlyList<string> Variants)
{
    public bool Installed => Variants.Count > 0;
}

/// <summary>
/// Which EPLAN years this computer has, read from the folders the installer makes
/// (<c>EPLAN\Electric P8\2026.0.3</c>, <c>EPLAN\Pro Panel\2026.0.3</c>); nothing is started or loaded. The Workbench
/// 4.0 design shows 2024 to 2027.
/// </summary>
internal static class EplanInstalls
{
    public static readonly int[] Years = [2024, 2025, 2026, 2027];

    private static readonly string[] _variants = ["Electric P8", "Pro Panel"];

    public static IReadOnlyList<EplanInstall> Scan(string eplanRoot) =>
        [.. Years.Select(year => new EplanInstall(year, [.. _variants.Where(v => Has(Path.Combine(eplanRoot, v), year))]))];

    private static bool Has(string variantFolder, int year)
    {
        try
        {
            return Directory.Exists(variantFolder) && Directory.EnumerateDirectories(variantFolder, year + ".*").Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
