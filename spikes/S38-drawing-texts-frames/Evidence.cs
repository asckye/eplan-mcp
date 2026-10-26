using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S38;

internal static class Evidence
{
    internal static string ScratchRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp", "scratch");

    // EPLAN may load the add-in from a shadow copy outside the repository, so inside EPLAN the search starts at the report path.
    internal static string Repository(string? start = null)
    {
        for (DirectoryInfo? directory = new(start ?? Path.GetDirectoryName(typeof(Evidence).Assembly.Location)!); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EplanMcp.slnx"))) { return directory.FullName; }
        }
        throw new DirectoryNotFoundException("Keep the S38 build output in this repository.");
    }

    internal static string CreateRunDirectory()
    {
        string directory = Repository();
        NoLinks(directory);
        foreach (string part in new[] { ".local", "spikes", "s38", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") })
        {
            directory = Path.Combine(directory, part);
            if (Directory.Exists(directory)) { NoLinks(directory); }
            else { Directory.CreateDirectory(directory); }
        }
        return directory;
    }

    internal static string ReportPath(string path)
    {
        string full = Path.GetFullPath(path);
        string reports = Path.Combine(Repository(Path.GetDirectoryName(full)), ".local", "spikes", "s38") + @"\";
        if (!full.StartsWith(reports, StringComparison.OrdinalIgnoreCase) || !string.Equals(Path.GetExtension(full), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("S38 requests/reports must be JSON files under this repository's .local/spikes/s38/.");
        }
        NoLinks(Path.GetDirectoryName(full)!);
        if (File.Exists(full)) { NoLinks(full); }
        if (File.Exists(full + ".partial")) { NoLinks(full + ".partial"); }
        return full;
    }

    internal static string ProjectPath(string path)
    {
        string full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\", StringComparison.Ordinal) || !full.StartsWith(ScratchRoot + @"\", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(full), ".elk", StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            throw new ArgumentException("Use an existing .elk copy under %USERPROFILE%\\EplanMcp\\scratch\\.");
        }

        NoLinks(full);
        string database = Path.ChangeExtension(full, ".edb");
        if (!Directory.Exists(database)) { throw new DirectoryNotFoundException("The scratch copy must include its .edb directory."); }
        NoLinks(database);
        return full;
    }

    internal static void NoLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArgumentException("Reparse points are refused: " + current);
            }
        }
    }

    // A17/A35: design files only. Keep User.* in the actual copy; exclude them only from the comparison.
    internal static JObject Hashes(string project)
    {
        var watch = Stopwatch.StartNew();
        ProjectPath(project);
        string database = Path.ChangeExtension(project, ".edb");
        var files = new List<string> { project };
        void Walk(string directory)
        {
            NoLinks(directory);
            foreach (string child in Directory.GetDirectories(directory)) { Walk(child); }
            foreach (string file in Directory.GetFiles(directory))
            {
                NoLinks(file);
                string name = Path.GetFileName(file);
                if (!name.StartsWith("User.", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".lck", StringComparison.OrdinalIgnoreCase)) { files.Add(file); }
            }
        }

        Walk(database);
        var manifest = new JObject();
        foreach (string file in files.OrderBy(p => p, StringComparer.Ordinal))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sha = SHA256.Create();
            manifest[file == project ? "project.elk" : file.Substring(database.Length + 1)] = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }

        return new JObject { ["files"] = manifest, ["elapsedMs"] = watch.Elapsed.TotalMilliseconds, ["excludes"] = "User.*, *.lck; no save/close/flush forced" };
    }

    internal static JObject CompareHashes(JToken before, JToken after)
    {
        var a = (JObject)before["files"]!;
        var b = (JObject)after["files"]!;
        string[] changed = a.Properties().Select(p => p.Name).Union(b.Properties().Select(p => p.Name), StringComparer.Ordinal)
            .Where(k => !JToken.DeepEquals(a[k], b[k])).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        return new JObject { ["equal"] = changed.Length == 0, ["changedFiles"] = new JArray(changed) };
    }

    internal static JObject Error(Exception ex) => new() { ["type"] = ex.GetType().FullName, ["message"] = ex.Message };

    internal static JToken Capture(JObject row, string name, Func<JToken> read)
    {
        var watch = Stopwatch.StartNew();
        try { return row[name] = read(); }
        catch (Exception ex) { return row[name] = new JObject { ["error"] = Error(ex) }; }
        finally { (row["timingsMs"] ??= new JObject())[name] = watch.Elapsed.TotalMilliseconds; }
    }

    internal static void Write(string path, JObject report)
    {
        string full = ReportPath(path);
        File.WriteAllText(full + ".partial", report.ToString(Formatting.Indented), new UTF8Encoding(false));
        // These are new, owned reports; copying destination ACL/metadata is unnecessary and can fail in a restricted host.
        if (File.Exists(full)) { File.Replace(full + ".partial", full, null, ignoreMetadataErrors: true); }
        else { File.Move(full + ".partial", full); }
    }
}
