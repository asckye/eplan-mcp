namespace EplanMcp.Packs.Tests;

/// <summary>Pack folders for the tests, written by hand: a macro file and a small project (link file and data folder).</summary>
internal static class PackFolders
{
    public const string Macro = "resources/macros/Motor.ema";
    public const string Project = "resources/projects/Sample/Sample.elk";

    /// <summary>Writes the files of a valid pack below <paramref name="folder"/>'s root and returns its manifest (also written).</summary>
    public static async Task<PackManifest> WriteAsync(TempFolder folder, Func<PackManifest, PackManifest>? change = null)
    {
        folder.Write(Macro, "<?xml version=\"1.0\"?><EplanPxfRoot Type=\"WindowMacro\" />");
        folder.Write(Project, "Sample.edb\n18.0\n");
        folder.Write("resources/projects/Sample/Sample.edb/ProjectInfo.xml", "<Properties />");
        folder.Write("resources/projects/Sample/Sample.edb/Page.eod", "pages");
        PackManifest manifest = Manifests.Minimal(
            await ResourceAsync(folder, Macro, ResourceKind.Macro),
            await ResourceAsync(folder, Project, ResourceKind.Project));
        manifest = change?.Invoke(manifest) ?? manifest;
        folder.Write(PackManifest.FileName, PackJson.Serialize(manifest));
        return manifest;
    }

    public static async Task<PackResource> ResourceAsync(TempFolder folder, string path, ResourceKind kind)
    {
        CancellationToken none = TestContext.Current.CancellationToken;
        (string sha256, long bytes, int files) = kind == ResourceKind.Project
            ? await PackHasher.HashTreeAsync(PackHasher.ProjectFiles(folder.PathOf(path)), none)
            : await FileAsync(folder.PathOf(path), none);
        return Manifests.File(path, kind) with { Sha256 = sha256, Bytes = bytes, Files = files };
    }

    private static async Task<(string, long, int)> FileAsync(string path, CancellationToken cancellationToken)
    {
        (string sha256, long bytes) = await PackHasher.HashFileAsync(path, cancellationToken);
        return (sha256, bytes, 1);
    }
}
