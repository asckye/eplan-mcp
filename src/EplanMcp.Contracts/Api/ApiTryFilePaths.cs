using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace EplanMcp.Contracts;
/// <summary>Shared SEC-19/20/31 file confinement for Host validation and compiled Bridge dispatch.</summary>
public static class ApiTryFilePaths
{
    /// <summary>Bounded file hashing, including input files bound to the approval fingerprint.</summary>
    public const long MaxBytes = 128 * 1024 * 1024;
    /// <summary>Checks the relative path, including Windows device names and alternate data streams.</summary>
    public static bool Valid(string file, bool output) => !string.IsNullOrWhiteSpace(file) && file.Length <= (output ? 100 : 260)
        && (!output || file.IndexOfAny(['/', '\\']) < 0 && file.IndexOf("..", StringComparison.Ordinal) < 0) && file.Split('/', '\\').All(s => s.Length > 0 && s is not ("." or "..")
            && !s.EndsWith(".", StringComparison.Ordinal) && !s.EndsWith(" ", StringComparison.Ordinal)
            && !s.Any(c => char.IsControl(c) || "<>:\"|?*".Contains(c))
            && !Regex.IsMatch(s.Split('.')[0].TrimEnd(' '), "\\A(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])\\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    /// <summary>Resolves a checked file; output directories are created only immediately before invocation.</summary>
    public static string Resolve(string root, string outputId, string file, bool output, bool create = false)
    {
        if (!Valid(file, output) || !System.IO.Path.IsPathRooted(root) || root.StartsWith(@"\\", StringComparison.Ordinal)
            || root.StartsWith("//", StringComparison.Ordinal) || !Regex.IsMatch(outputId, "\\A[0-9a-f]{32}\\z"))
        { throw Invalid(); }
        string folder = System.IO.Path.Combine(root, output ? "exports" : "imports");
        if (output)
        { folder = System.IO.Path.Combine(folder, "api-try-" + outputId); }
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, file.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        if (path.Length > 260)
        { throw Invalid(); }
        CheckLinks(path);
        if (output)
        {
            if (File.Exists(path) || Directory.Exists(path) || !create && Directory.Exists(folder))
            { throw Invalid(); }
            if (create)
            { Directory.CreateDirectory(folder); CheckLinks(path); }
        }
        else if (!File.Exists(path))
        { throw new ArgumentException("The trial input file is missing from the Host imports folder."); }
        return path;
    }
    private static void CheckLinks(string path)
    {
        for (string? step = path; !string.IsNullOrEmpty(step); step = System.IO.Path.GetDirectoryName(step))
        {
            try
            {
                if ((File.GetAttributes(step) & FileAttributes.ReparsePoint) != 0)
                { throw Invalid(); }
            }
            catch (FileNotFoundException) { /* Missing output paths are checked before creation. */ }
            catch (DirectoryNotFoundException) { /* Missing parents are checked before creation. */ }
        }
    }
    /// <summary>Hashes one bounded, non-linked file.</summary>
    public static ApiTryFile Describe(string path, string relative)
    {
        CheckLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxBytes)
        { throw Invalid(); }
        using var sha = SHA256.Create();
        return new() { Path = relative.Replace('\\', '/'), Bytes = stream.Length, Sha256 = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() };
    }
    /// <summary>Lists every file in this call's output folder, refusing links and excessive artifacts.</summary>
    public static List<ApiTryFile> Outputs(string root, string outputId)
    {
        string prefix = "api-try-" + outputId;
        string folder = System.IO.Path.Combine(root, "exports", prefix);
        CheckLinks(folder);
        var pending = new Stack<string>();
        pending.Push(folder);
        var files = new List<ApiTryFile>();
        int entries = 0;
        while (pending.Count > 0)
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                if (++entries > 100 || path.Length > 260 || !Valid(path.Substring(folder.Length + 1), false))
                { throw Invalid(); }
                CheckLinks(path);
                if (Directory.Exists(path))
                { pending.Push(path); }
                else
                { files.Add(Describe(path, prefix + "/" + path.Substring(folder.Length + 1))); }
            }
        }
        return files.OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
    }
    private static ArgumentException Invalid() => new("Trial files require bounded relative paths under Host imports/exports, no links, reserved names or existing output destinations.");
}
