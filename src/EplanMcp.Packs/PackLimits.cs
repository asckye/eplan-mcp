namespace EplanMcp.Packs;

/// <summary>
/// What an <c>.eplanpack</c> may hold before EplanMcp refuses to extract it: a bound on a ZIP bomb or a damaged file.
/// The defaults leave room for large project backups (the sample project's <c>.zw1</c> is 70 MB, its data folder 213 MB).
/// </summary>
public sealed record PackLimits
{
    public static PackLimits Default { get; } = new();

    public int MaxEntries { get; init; } = 100_000;

    public long MaxEntryBytes { get; init; } = 8L * 1024 * 1024 * 1024;

    public long MaxTotalBytes { get; init; } = 32L * 1024 * 1024 * 1024;
}
