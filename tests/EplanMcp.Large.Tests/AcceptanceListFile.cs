using System.Text;
using System.Text.Json;

namespace EplanMcp.Large.Tests;

internal static class AcceptanceListFile
{
    internal const string Variable = "EPLANMCP_ACCEPTANCE_LIST";

    internal static async Task<JsonElement> LoadAsync(string repository, string fixture, string? external, CancellationToken cancellationToken)
    {
        string path = fixture;
        if (!string.IsNullOrEmpty(external))
        {
            if (!Path.IsPathFullyQualified(external) || external.StartsWith(@"\\", StringComparison.Ordinal))
            { throw new InvalidOperationException("EPLANMCP_ACCEPTANCE_LIST must be an absolute local file path, outside the repository."); }
            path = Path.GetFullPath(external);
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository));
            if (path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            { throw new InvalidOperationException("External acceptance lists must remain outside the repository."); }
            for (FileSystemInfo? item = new FileInfo(path); item is not null; item = item is FileInfo file ? file.Directory : ((DirectoryInfo)item).Parent)
            {
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                { throw new InvalidOperationException("Acceptance list paths must not traverse symbolic links or junctions."); }
            }
        }
        if (new FileInfo(path).Length > AcceptanceDrawingList.MaxBytes)
        { throw new JsonException("/: drawing list exceeds 256 KiB"); }
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        string json = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        return AcceptanceDrawingList.Read(json);
    }
}
