using System.Globalization;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S40;

internal static class Evidence
{
    internal static string Target => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp", "scratch", "writes", "LargeWrite.elk");
    internal static string Repository(string start)
    {
        for (DirectoryInfo? dir = new(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EplanMcp.slnx"))) { return dir.FullName; }
        }
        throw new DirectoryNotFoundException("Keep reports and the driver inside the worktree.");
    }
    internal static void NoLinks(string path)
    {
        for (string? entry = Path.GetFullPath(path); entry is not null; entry = Path.GetDirectoryName(entry))
        {
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) { throw new ArgumentException("Links and junctions are refused."); }
        }
    }
    internal static void ProjectPath(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || !string.Equals(Path.GetFullPath(path), Target, StringComparison.OrdinalIgnoreCase)) { throw new ArgumentException("Only %USERPROFILE%/EplanMcp/scratch/writes/LargeWrite.elk is allowed."); }
        NoLinks(path);
        NoLinks(Path.ChangeExtension(path, ".edb"));
        if (!File.Exists(path) || !Directory.Exists(Path.ChangeExtension(path, ".edb"))) { throw new ArgumentException("A complete existing scratch project is required."); }
    }
    internal static string LogPath(string path, bool createParents)
    {
        string full = Path.GetFullPath(path);
        string root = Repository(Path.GetDirectoryName(full)!);
        string prefix = Path.Combine(root, ".local", "spikes", "s40") + Path.DirectorySeparatorChar;
        if (full.IndexOfAny(['"', '\r', '\n']) >= 0 || !full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(full), ".jsonl", StringComparison.OrdinalIgnoreCase) || File.Exists(full)) { throw new ArgumentException("Use a new JSONL under .local/spikes/s40/."); }
        NoLinks(root);
        string current = root;
        string[] parts = full.Substring(root.Length + 1).Split(Path.DirectorySeparatorChar);
        foreach (string part in parts.Take(parts.Length - 1))
        {
            current = Path.Combine(current, part);
            if (!Directory.Exists(current) && createParents) { Directory.CreateDirectory(current); }
            if (Directory.Exists(current)) { NoLinks(current); }
            else if (!createParents) { break; }
        }
        if (createParents || Directory.Exists(Path.GetDirectoryName(full)!)) { NoLinks(Path.GetDirectoryName(full)!); }
        return full;
    }
    internal static JObject Hashes(string project)
    {
        ProjectPath(project);
        string database = Path.ChangeExtension(project, ".edb");
        var result = new JObject { ["design"] = new JObject(), ["user"] = new JObject(), ["settings"] = new JObject() };
        void Walk(string directory)
        {
            NoLinks(directory);
            foreach (string child in Directory.GetDirectories(directory).OrderBy(x => x, StringComparer.Ordinal)) { Walk(child); }
            foreach (string file in Directory.GetFiles(directory).OrderBy(x => x, StringComparer.Ordinal)) { Add(file, file.Substring(database.Length + 1)); }
        }
        void Add(string file, string key)
        {
            NoLinks(file);
            string name = Path.GetFileName(file);
            if (name.EndsWith(".lck", StringComparison.OrdinalIgnoreCase)) { return; }
            string group = name.StartsWith("User.", StringComparison.OrdinalIgnoreCase) ? "user" : name.StartsWith("Settings.", StringComparison.OrdinalIgnoreCase) ? "settings" : "design";
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sha = SHA256.Create();
            result[group]![key] = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }
        Add(project, "project.elk");
        Walk(database);
        return result;
    }
    internal static JObject Error(Exception ex) => new() { ["type"] = ex.GetType().FullName, ["message"] = ex.Message };
    internal static string Number(int number) => number.ToString(CultureInfo.InvariantCulture);
}
