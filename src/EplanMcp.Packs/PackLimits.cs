namespace EplanMcp.Packs;

/// <summary>
/// What an <c>.eplanpack</c> may hold before EplanMcp refuses to extract it (ADR-0024 section 2): a bound on ZIP bombs and
/// damaged files. The defaults are ADR-0024's maxima; configuration may only lower them, so <see cref="Capped"/> is what
/// the extractor applies.
/// </summary>
public sealed record PackLimits
{
    private const long Gigabyte = 1024L * 1024 * 1024;

    public static PackLimits Default { get; } = new();

    /// <summary>The <c>.eplanpack</c> file itself, compressed.</summary>
    public long MaxArchiveBytes { get; init; } = 2 * Gigabyte;

    public int MaxEntries { get; init; } = 20_000;

    public long MaxEntryBytes { get; init; } = 2 * Gigabyte;

    public long MaxTotalBytes { get; init; } = 8 * Gigabyte;

    /// <summary>Uncompressed to compressed size of one entry.</summary>
    public int MaxCompressionRatio { get; init; } = 200;

    /// <summary>These limits, none above <see cref="Default"/>.</summary>
    public PackLimits Capped => new()
    {
        MaxArchiveBytes = Math.Min(MaxArchiveBytes, Default.MaxArchiveBytes),
        MaxEntries = Math.Min(MaxEntries, Default.MaxEntries),
        MaxEntryBytes = Math.Min(MaxEntryBytes, Default.MaxEntryBytes),
        MaxTotalBytes = Math.Min(MaxTotalBytes, Default.MaxTotalBytes),
        MaxCompressionRatio = Math.Min(MaxCompressionRatio, Default.MaxCompressionRatio),
    };
}
