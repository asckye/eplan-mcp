namespace EplanMcp.Packs.Scanning;

/// <summary>
/// A resource the scanner found: its manifest entry, the facts read from its files, how it was given, and where its
/// files are on disk (<see cref="FilesOnDisk"/>: relative to the resource's folder, full path; only for copying them).
/// </summary>
public sealed record ScannedResource(
    PackResource Resource,
    IReadOnlyList<PackFact> Facts,
    ScanInput Input,
    IReadOnlyList<(string Relative, string FullPath)> FilesOnDisk);
