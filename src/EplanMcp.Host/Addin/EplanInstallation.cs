namespace EplanMcp.Host.Addin;

/// <summary>
/// An EPLAN version found as doctor finds it, <c>&lt;Program Files&gt;\EPLAN\Platform\&lt;folder&gt;\Bin\EPLAN.exe</c>, with
/// its file version and the variants installed for it: each <c>&lt;Program Files&gt;\EPLAN\&lt;variant&gt;\&lt;folder&gt;\Bin</c>
/// that has <c>W3u.exe</c> (api-notes A22; Electric P8 and Pro Panel 2026 on the dev machine).
/// </summary>
internal sealed record EplanInstallation(string Exe, string Version, IReadOnlyList<string> Variants)
{
    /// <summary>The Platform's version folder, such as <c>2026.0.3</c>, which the variants and the station settings use too.</summary>
    public string Folder => FolderOf(Exe);

    /// <summary>Every <c>EPLAN.exe</c> under the Platform folder, in name order.</summary>
    public static IReadOnlyList<string> Executables(string programFiles)
    {
        string platform = Path.Combine(programFiles, "EPLAN", "Platform");
        return Directory.Exists(platform)
            ? [.. Directory.EnumerateDirectories(platform).Select(d => Path.Combine(d, "Bin", "EPLAN.exe")).Where(File.Exists).Order(StringComparer.OrdinalIgnoreCase)]
            : [];
    }

    /// <summary>The installations, reading each file version with <paramref name="fileVersion"/> ("?" when it has none).</summary>
    public static IReadOnlyList<EplanInstallation> Find(string programFiles, Func<string, string?> fileVersion)
    {
        string eplan = Path.Combine(programFiles, "EPLAN");
        return [.. Executables(programFiles).Select(exe =>
        {
            string folder = FolderOf(exe);
            string[] variants =
            [
                .. Directory.EnumerateDirectories(eplan)
                    .Where(d => File.Exists(Path.Combine(d, folder, "Bin", "W3u.exe")))
                    .Select(Path.GetFileName)
                    .OfType<string>()
                    .Where(name => !name.Equals("Platform", StringComparison.OrdinalIgnoreCase))
                    .Order(StringComparer.OrdinalIgnoreCase),
            ];
            return new EplanInstallation(exe, fileVersion(exe) ?? "?", variants);
        })];
    }

    private static string FolderOf(string exe) => Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(exe)))!;
}
