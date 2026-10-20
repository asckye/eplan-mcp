using System.Globalization;

namespace EplanMcp.Host.Exports;

/// <summary>File sizes as summaries and doctor show them: bytes, whole KB, then MB and GB with one decimal (1 KB = 1024 bytes).</summary>
internal static class ByteSize
{
    private const double Kilo = 1024;

    public static string Format(long bytes) => bytes switch
    {
        < 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes} bytes"),
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / Kilo:0} KB"),
        < 1024L * 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (Kilo * Kilo):0.#} MB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (Kilo * Kilo * Kilo):0.#} GB"),
    };
}
