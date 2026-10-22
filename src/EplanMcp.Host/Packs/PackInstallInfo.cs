using EplanMcp.Packs;

namespace EplanMcp.Host.Packs;

/// <summary>
/// What the engineer confirms before someone else's <c>.eplanpack</c> is installed (ADR-0024 section 4): the pack and
/// version, its author as the manifest names it (not checked: packs are unsigned in 0.x), the digest and size, its EPLAN
/// files by name, its requirements, and whether it references resources outside itself. All pack text is data.
/// </summary>
internal sealed record PackInstallInfo(
    string File, string Id, string Version, string? Author, string Digest, long Bytes, IReadOnlyList<string> NativeFiles, IReadOnlyList<string> Capabilities, int Referenced)
{
    /// <summary>The EPLAN files the dialog names at most; the rest are counted.</summary>
    public const int MaxNamed = 20;

    public static PackInstallInfo From(string file, PackManifest manifest, string digest, long bytes) => new(
        file,
        manifest.Id,
        manifest.Version,
        manifest.Provenance.Author,
        digest,
        bytes,
        [.. manifest.Resources.Where(r => r.Kind is ResourceKind.Macro or ResourceKind.Project or ResourceKind.ProjectBackup or ResourceKind.BasicProject or ResourceKind.Form or ResourceKind.Frame)
            .Select(r => $"{AssetNames.KindOf(AssetNames.Local(r))}: {r.Path}")],
        [.. manifest.Compatibility.Capabilities],
        manifest.Resources.Count(r => r.Storage == ResourceStorage.Referenced));
}
