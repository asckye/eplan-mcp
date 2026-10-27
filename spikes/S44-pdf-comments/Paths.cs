using System.Security.Cryptography;

namespace EplanMcp.Spikes.S44;

internal static class Paths
{
    internal static string Scratch => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp", "scratch");
    internal static string Project => Path.Combine(Scratch, "writes", "LargeWrite.elk");
    internal static string Runs => Path.Combine(Scratch, "spikes", "s44");

    internal static string Checked(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOfAny(['\"', '\r', '\n']) >= 0 || path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new ArgumentException("A local path without quotes or newlines is required.");
        }

        string full = Path.GetFullPath(path);
        if (full.IndexOf(':', 2) >= 0)
        {
            throw new ArgumentException("Alternate data streams are not allowed.");
        }

        return full;
    }

    internal static bool IsProject(string path) => string.Equals(Checked(path), Project, StringComparison.OrdinalIgnoreCase);

    internal static void ProjectFiles(string path)
    {
        if (!IsProject(path) || !File.Exists(Project) || !Directory.Exists(Path.ChangeExtension(Project, ".edb")))
        {
            throw new InvalidOperationException("Only the existing scratch/writes/LargeWrite.elk project is allowed.");
        }

        RejectLinks(Project);
        RejectLinks(Path.ChangeExtension(Project, ".edb"));
    }

    internal static string Input(string path)
    {
        string full = Checked(path);
        if (!full.StartsWith(Path.Combine(Scratch, "inputs") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(full), ".pdf", StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            throw new ArgumentException("--pdf must be an existing PDF under scratch/inputs.");
        }

        RejectLinks(full);
        return full;
    }

    internal static void RunFile(string path)
    {
        string full = Checked(path);
        string? directory = Path.GetDirectoryName(full);
        if (directory is null || !string.Equals(Path.GetDirectoryName(directory), Runs, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Run files must be directly inside a run folder under scratch/spikes/s44.");
        }

        RejectLinks(full);
    }

    internal static void RejectLinks(string path)
    {
        for (string? entry = Checked(path); entry is not null; entry = Path.GetDirectoryName(entry))
        {
            if ((File.Exists(entry) || Directory.Exists(entry)) && (File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Paths must not traverse a symbolic link or junction.");
            }
        }
    }

    internal static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
    }
}
