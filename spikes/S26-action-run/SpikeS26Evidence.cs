using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.MasterData;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S26;

public sealed partial class SpikeS26Action
{
    private const int ReportLimit = 290 * 1024;

    private static JObject Counts(Project project)
    {
        using var finder = new DMObjectsFinder(project);
        var counts = new JObject();
        // S34 counts copied without disposing ANY finder result or project-owned object (A39).
        Capture(counts, "pages", () => new JValue(finder.GetPages(null).Length));
        Capture(counts, "functions", () => new JValue(finder.GetFunctions(null).Length));
        Capture(counts, "connections", () => new JValue(finder.GetConnections(null).Length));
        Capture(counts, "cables", () =>
        {
            using var filter = new FunctionsFilter { FunctionCategory = FunctionCategory.Cable };
            return new JValue(finder.GetFunctions(filter).Length);
        });
        Capture(counts, "plcs", () => new JValue(finder.GetPLCs(null).Length));
        Capture(counts, "plcTerminals", () => new JValue(finder.GetTerminals(null).Count(terminal => terminal.PLC is not null)));
        Capture(counts, "articles", () => new JValue(project.Articles.Length));
        Capture(counts, "articleReferences", () => new JValue(finder.GetArticleReferences(null).Length));
        Capture(counts, "planningSegments", () => new JValue(finder.GetPlanningSegments(null).Length));
        Capture(counts, "comments", () => new JValue(finder.GetAll<Comment>(true).Length));
        Capture(counts, "deviceListEntries", () => new JValue(finder.GetAll<DeviceListEntry>(true).Length));
        Capture(counts, "terminals", () => new JValue(finder.GetTerminals(null).Length));
        Capture(counts, "terminalStrips", () => new JValue(finder.GetTerminalStrips(null).Length));
        return counts;
    }

    private static JObject Stage(string[] paths, Dictionary<string, FileSnapshot> baseline, string? layerName, bool parts = false)
    {
        var result = new JObject();
        Capture(result, "currentProject", () => new JValue(CurrentProjectPath()));
        Capture(result, "openProjects", () => new JArray(OpenProjectPaths()));
        using var manager = new ProjectManager();
        var projects = new JObject();
        result["projects"] = projects;
        foreach (string path in paths)
        {
            var row = new JObject();
            projects[path] = row;
            Capture(row, "counts", () => Counts(FindProject(manager, path)));
            Capture(row, "files", () => Summary(ProjectFiles(path), baseline[path], false));
            if (layerName is not null) { Capture(row, "layer", () => ReadLayer(FindProject(manager, path), layerName)); }
        }

        if (parts) { Capture(result, "partsDatabase", PartsState); }
        return result;
    }

    private static JObject ReadLayer(Project project, string name)
    {
        // Both are borrowed project data, even though their wrappers also implement IDisposable.
        var layer = project.LayerTable.Layers.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        return layer is null ? new JObject { ["name"] = name, ["exists"] = false } :
            new JObject { ["name"] = name, ["exists"] = true, ["visible"] = layer.isVisible, ["printed"] = layer.isPrinted, ["textHeight"] = layer.TextHeight };
    }

    private static FileSnapshot ProjectFiles(string path) => ReadFiles(Path.ChangeExtension(SpikeS26Paths.Project(path), ".edb"));

    private static FileSnapshot ReadFiles(string directory)
    {
        var snapshot = new FileSnapshot();
        foreach (string path in SafeFiles(directory))
        {
            if (path.EndsWith(".lck", StringComparison.OrdinalIgnoreCase)) { continue; }
            snapshot.Entries[path.Substring(directory.Length).TrimStart('\\')] = FileStamp(path);
        }
        return snapshot;
    }

    private static IEnumerable<string> SafeFiles(string directory)
    {
        if (!Directory.Exists(directory)) { throw new DirectoryNotFoundException("Evidence directory is absent: " + directory); }
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) { throw new IOException("Evidence directory contains a reparse point."); }
            foreach (string item in Directory.EnumerateFileSystemEntries(current))
            {
                FileAttributes attributes = File.GetAttributes(item);
                if ((attributes & FileAttributes.ReparsePoint) != 0) { throw new IOException("Evidence path contains a reparse point."); }
                if ((attributes & FileAttributes.Directory) != 0) { pending.Push(item); }
                else { yield return item; }
            }
        }
    }

    private static JObject FileStamp(string path)
    {
        var row = new JObject();
        try
        {
            var before = new FileInfo(path);
            long length = before.Length;
            DateTime modified = before.LastWriteTimeUtc;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sha = SHA256.Create();
            row["sha256"] = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
            row["bytes"] = length;
            row["lastWriteUtc"] = modified.ToString("o", CultureInfo.InvariantCulture);
            before.Refresh();
            row["changedDuringHash"] = before.Length != length || before.LastWriteTimeUtc != modified;
        }
        catch (Exception ex) { row["error"] = Describe(ex); }
        return row;
    }

    private static JObject Summary(FileSnapshot files, FileSnapshot baseline, bool includeAll)
    {
        string[] changed = files.Entries.Keys.Union(baseline.Entries.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(name => !files.Entries.TryGetValue(name, out JObject? a) || !baseline.Entries.TryGetValue(name, out JObject? b) || !JToken.DeepEquals(a, b))
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();
        string[] names = includeAll ? files.Entries.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray() : changed;
        int limit = includeAll ? 128 : 4;
        var entries = new JArray();
        foreach (string name in names.Take(limit))
        {
            JObject row = files.Entries.TryGetValue(name, out JObject? stamp) ? (JObject)stamp.DeepClone() : new JObject { ["deleted"] = true };
            row["name"] = name;
            entries.Add(row);
        }

        return new JObject
        {
            ["total"] = files.Entries.Count, ["hashErrors"] = files.Entries.Values.Count(row => row["error"] is not null),
            ["unstableFiles"] = files.Entries.Values.Count(row => row.Value<bool?>("changedDuringHash") == true),
            ["manifestSha256"] = Digest(files.Entries, false, false),
            ["contentManifestSha256"] = Digest(files.Entries, true, false),
            ["designContentManifestSha256"] = Digest(files.Entries, true, true),
            ["changedFromModeBefore"] = changed.Length, ["entriesOmitted"] = Math.Max(0, names.Length - limit), ["entries"] = entries,
        };
    }

    private static string Digest(Dictionary<string, JObject> files, bool contentOnly, bool excludeUser)
    {
        string manifest = string.Join("\n", files.Where(pair => !excludeUser || !IsUserFile(pair.Key)).OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key + "=" + (contentOnly ? pair.Value["sha256"]?.Value<string>() ?? pair.Value.ToString(Formatting.None) : pair.Value.ToString(Formatting.None))));
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(manifest))).Replace("-", string.Empty);
    }

    private static bool IsUserFile(string name) => string.Equals(Path.GetFileName(name), "User.eod", StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileName(name), "User.eox", StringComparison.OrdinalIgnoreCase);

    private static JObject OutputFiles(string folder)
    {
        if (!Directory.Exists(folder)) { return new JObject { ["total"] = 0, ["entries"] = new JArray(), ["entriesOmitted"] = 0 }; }
        FileSnapshot files = ReadFiles(folder);
        JObject summary = Summary(files, files, true);
        // S34's 16 output rows; all produced files still participate in hashing and total statistics.
        JArray entries = (JArray)summary["entries"]!;
        while (entries.Count > 16) { entries.RemoveAt(entries.Count - 1); }
        summary["entriesOmitted"] = Math.Max(0, files.Entries.Count - entries.Count);
        return summary;
    }

    private static bool ConfiguredPartsFile(out string path, out string reason)
    {
        path = string.Empty;
        try
        {
            string configured = MDPartsManagement.SelectedPartsDatabaseAsString;
            string expanded = PathMap.SubstitutePath(configured);
            // Do not interpret a SQL/Cloud connection string as a path or connect to it.
            if (string.IsNullOrWhiteSpace(expanded) || !Path.IsPathRooted(expanded) || expanded.StartsWith(@"\\", StringComparison.Ordinal) ||
                !string.Equals(Path.GetExtension(expanded), ".alk", StringComparison.OrdinalIgnoreCase) || !File.Exists(expanded))
            {
                reason = "not run: configured parts database is not a readable local .alk file; SQL/Cloud/network databases are outside this spike.";
                return false;
            }
            path = Path.GetFullPath(expanded);
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) { reason = "not run: configured parts database discovery failed: " + Describe(ex).ToString(Formatting.None); return false; }
    }

    private static JObject PartsState()
    {
        var result = new JObject { ["configured"] = MDPartsManagement.SelectedPartsDatabaseAsString };
        if (!ConfiguredPartsFile(out string path, out string reason)) { result["unavailable"] = reason; return result; }
        result["path"] = path;
        // OpenDatabase() uses the configured library. NEVER call its path overload or change any setting.
        var management = new MDPartsManagement();
        MDPartsDatabase database = management.OpenDatabase();
        try
        {
            var counts = new JObject();
            Capture(counts, "parts", () => new JValue(database.Parts.Length));
            Capture(counts, "manufacturers", () => new JValue(database.Manufacturers.Length));
            Capture(counts, "constructions", () => new JValue(database.Constructions.Length));
            Capture(counts, "connectionPatterns", () => new JValue(database.ConnectionPointInfos.Length));
            Capture(counts, "accessoryLists", () => new JValue(database.AccessoryLists.Length));
            Capture(counts, "accessoryPlacements", () => new JValue(database.AccessoryPlacements.Length));
            result["counts"] = counts;
        }
        finally { database.Close(); } // Explicit helper close, never Dispose database records (A39).
        string directory = Path.ChangeExtension(path, ".adb");
        FileSnapshot files = ReadFiles(directory);
        files.Entries["../" + Path.GetFileName(path)] = FileStamp(path);
        result["files"] = Summary(files, files, true);
        return result;
    }

    private static JObject Assess(JObject result, string[] paths, Candidate c)
    {
        var comparisons = new JObject();
        foreach (string stage in new[] { "after", "afterRollback", "afterSafetyPointDisposed" })
        {
            var rows = new JArray();
            comparisons[stage] = rows;
            foreach (string path in paths)
            {
                JToken? before = result["before"]?["projects"]?[path];
                JToken? after = result[stage]?["projects"]?[path];
                var row = new JObject { ["project"] = path };
                rows.Add(row);
                row["countsChanged"] = Changed(before?["counts"], after?["counts"]);
                row["fileHashesChanged"] = Changed(before?["files"]?["contentManifestSha256"], after?["files"]?["contentManifestSha256"]);
                row["designFileHashesChanged"] = Changed(before?["files"]?["designContentManifestSha256"], after?["files"]?["designContentManifestSha256"]);
                row["fileMetadataChanged"] = Changed(before?["files"]?["manifestSha256"], after?["files"]?["manifestSha256"]);
                if (c.LayerName is not null) { row["layerChanged"] = Changed(before?["layer"], after?["layer"]); }
                row["hashComparisonComplete"] = before?["files"]?.Value<int?>("hashErrors") == 0 && after?["files"]?.Value<int?>("hashErrors") == 0 &&
                    before?["files"]?.Value<int?>("unstableFiles") == 0 && after?["files"]?.Value<int?>("unstableFiles") == 0;
            }
        }
        if (c.PartsDatabase)
        {
            var parts = new JObject();
            comparisons["partsDatabase"] = parts;
            foreach (string stage in new[] { "after", "afterRollback", "afterSafetyPointDisposed" })
            {
                JToken? before = result["before"]?["partsDatabase"];
                JToken? after = result[stage]?["partsDatabase"];
                parts[stage] = new JObject
                {
                    ["configurationChanged"] = Changed(before?["configured"], after?["configured"]),
                    ["countsChanged"] = Changed(before?["counts"], after?["counts"]),
                    ["fileHashesChanged"] = Changed(before?["files"]?["contentManifestSha256"], after?["files"]?["contentManifestSha256"]),
                    ["dataFileHashesChanged"] = Changed(before?["files"]?["designContentManifestSha256"], after?["files"]?["designContentManifestSha256"]),
                };
            }
        }
        comparisons["limitation"] = "Fingerprint only; changes are not individually verified. Equal counts do not prove unchanged properties; file buffers/background writes can outlive rollback. User.eod/eox included in S34 digest and excluded only from the additional ADR/A30 data digest.";
        return comparisons;
    }

    private static JValue Changed(JToken? before, JToken? after) => before is null || after is null || HasError(before) || HasError(after)
        ? JValue.CreateNull() : new JValue(!JToken.DeepEquals(before, after));

    private static bool HasError(JToken value) => value is JObject obj && (obj["error"] is not null || obj.Descendants().OfType<JProperty>().Any(property => property.Name == "error" || property.Name == "errorRef"));

    private static JObject FileNameCheck(Candidate c)
    {
        string expected = c.ExpectedRelative!.Replace('/', '\\');
        string[] produced = SafeFiles(c.Folder).Select(path => path.Substring(c.Folder.Length).TrimStart('\\')).ToArray();
        bool windowsValid = !expected.Split('\\').Any(part => part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0);
        return new JObject
        {
            ["requestedValue"] = c.Options["EXPORTFILE"], ["expectedNormalizedRelative"] = expected,
            ["windowsFileNameValid"] = windowsValid, ["exactProducedPath"] = produced.Contains(expected, StringComparer.Ordinal),
            ["producedNames"] = new JArray(produced),
            ["interpretation"] = windowsValid ? "Compare actual path with the unchanged context value; slash is a separator." : "Literal ASCII double-quote file names cannot exist on Windows. Rejection/sanitization is not evidence of command-line reparsing; this part of runner question (a) remains limited.",
        };
    }

    private static JObject MessagesSince(int before)
    {
        // A11/S34: never read the old collection again after constructing this one.
        var messages = new SysMessagesCollection();
        var entries = new JArray();
        int index = 0;
        foreach (object item in messages)
        {
            if (index++ >= before && entries.Count < 6 && item is BaseException message)
            {
                entries.Add(new JObject { ["level"] = (int)message.MessageLevel, ["text"] = Cut(message.GetMessageText() ?? string.Empty, 500) });
            }
        }
        return new JObject { ["beforeCount"] = before, ["afterCount"] = messages.Count, ["collectionReset"] = messages.Count < before, ["entries"] = entries, ["entriesOmitted"] = Math.Max(0, messages.Count - before - entries.Count) };
    }

    private static void Capture(JObject target, string name, Func<JToken> probe)
    {
        try { target[name] = probe(); }
        catch (Exception ex) { target[name] = new JObject { ["error"] = Describe(ex) }; }
    }

    private static void WriteReport(string path, JObject source)
    {
        try
        {
            // Work on a clone: progress compaction must not replace the live evidence with error references.
            var report = (JObject)source.DeepClone();
            var errors = new JObject();
            JObject[] descriptions = report.Descendants().OfType<JObject>().Where(obj => obj["type"] is JValue && obj["message"] is JValue).ToArray();
            foreach (IGrouping<string, JObject> group in descriptions.GroupBy(obj => obj.ToString(Formatting.None), StringComparer.Ordinal).Where(group => group.Count() > 1))
            {
                string key = "E" + errors.Count.ToString(CultureInfo.InvariantCulture);
                errors[key] = group.First().DeepClone();
                foreach (JObject description in group) { description.Replace(new JObject { ["errorRef"] = key }); }
            }
            if (errors.Count > 0) { report["exceptionDefinitions"] = errors; }
            string json = report.ToString(Formatting.None);
            while (Encoding.UTF8.GetByteCount(json) > ReportLimit)
            {
                JArray? largest = report.Descendants().OfType<JArray>().Where(array => array.Count > 0 && array.Parent is JProperty property &&
                    property.Name != "attempts" && property.Name != "openProjects" && property.Name != "producedNames" && property.Name != "after" && property.Name != "afterRollback" && property.Name != "afterSafetyPointDisposed")
                    .OrderByDescending(array => array.ToString(Formatting.None).Length).FirstOrDefault();
                if (largest is null) { throw new InvalidOperationException("S26 report exceeded its diagnostic budget."); }
                largest.RemoveAt(largest.Count - 1);
                if (largest.Parent?.Parent is JObject owner && owner["entriesOmitted"] is JValue omitted) { owner["entriesOmitted"] = omitted.Value<int>() + 1; }
                report["additionalListTruncation"] = true;
                json = report.ToString(Formatting.None);
            }
            string full = SpikeS26Paths.Below(path, SpikeS26Paths.OutputRoot);
            string partial = full + ".partial";
            File.WriteAllText(partial, json, new UTF8Encoding(false));
            if (File.Exists(full)) { File.Replace(partial, full, null); }
            else { File.Move(partial, full); }
        }
        catch (Exception ex) { Trace.WriteLine("S26 report write failed: " + Describe(ex).ToString(Formatting.None)); }
    }

    private static bool MainWindowDisabled()
    {
        using var process = Process.GetCurrentProcess();
        IntPtr window = process.MainWindowHandle;
        return window != IntPtr.Zero && !IsWindowEnabled(window);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);

    private static JObject Describe(Exception ex) => new() { ["type"] = ex.GetType().FullName, ["message"] = Cut(ex.Message, 700), ["inner"] = Cut(ex.InnerException?.Message ?? string.Empty, 300) };
    private static string Cut(string value, int length) => value.Length <= length ? value : value.Substring(0, length) + "...";

    private sealed class FileSnapshot
    {
        internal Dictionary<string, JObject> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    // Poll only Win32 window state; never access EPLAN API or data objects from this timer thread.
    private sealed class WindowWatch : IDisposable
    {
        private readonly IntPtr _window;
        private readonly Timer _timer;
        private int _disabled;
        private int _samples;

        internal WindowWatch()
        {
            using var process = Process.GetCurrentProcess();
            _window = process.MainWindowHandle;
            _timer = new Timer(_ =>
            {
                if (_window != IntPtr.Zero)
                {
                    Interlocked.Increment(ref _samples);
                    if (!IsWindowEnabled(_window)) { Interlocked.Exchange(ref _disabled, 1); }
                }
            }, null, 0, 50);
        }

        internal JObject Snapshot() => new()
        {
            ["mainWindowDisabledObserved"] = Volatile.Read(ref _disabled) != 0,
            ["samples"] = Volatile.Read(ref _samples), ["intervalMs"] = 50,
            ["windowFound"] = _window != IntPtr.Zero,
            ["limitation"] = "Disabled state suggests modal UI; sampling can miss short/modeless dialogs. Engineer observation is required; absence of a sample is not proof that no dialog appeared.",
        };

        public void Dispose() => _timer.Dispose();
    }
}
