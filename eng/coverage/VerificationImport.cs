using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EplanMcp.Coverage.Mapping;
using EplanMcp.Coverage.Reporting;
using EplanMcp.Coverage.Scanning;
using YamlDotNet.RepresentationModel;

namespace EplanMcp.Coverage;

/// <summary>Imports detached evidence, preserving all manually reviewed overlay text outside verification.</summary>
internal static class VerificationImport
{
    internal const string WhitelistPath = "catalog/mapping/verified-whitelist.yaml";
    private static readonly JsonSerializerOptions _rowJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly Regex _section = new(@"(?m)^\S", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    internal static Task ImportAsync(string root, params string[] paths)
    {
        if (paths.Length == 0)
        { throw new ArgumentException("Supply at least one results.json file.", nameof(paths)); }
        VerificationRun[] runs = [.. paths.Select(CoverageJson.Read<VerificationRun>)];
        BridgeUsage scan = CoverageJson.Read<BridgeUsage>(Path.Combine(root, BridgeUsage.RelativePath));
        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        string path = Path.Combine(root, Overlay.RelativePath);
        string merged = MergeText(File.ReadAllText(path), runs, EligibleIds(root), scan.EplanApi, id => unknown.Add(id));
        File.WriteAllText(path, merged);
        foreach (string id in unknown)
        { Console.WriteLine("Dropped unknown verification ID: " + id); }
        Console.WriteLine("Dropped unknown verification IDs: " + unknown.Count.ToString(CultureInfo.InvariantCulture));
        return Task.CompletedTask;
    }

    internal static Dictionary<string, string> EligibleIds(string root)
    {
        BridgeUsage scan = CoverageJson.Read<BridgeUsage>(Path.Combine(root, BridgeUsage.RelativePath));
        ApiCatalog catalog = ApiCatalog.Load(Path.Combine(root, "catalog/eplan-" + string.Join('.', scan.EplanApi.Split('.').Take(3))));
        Overlay overlay = Overlay.Load(root, [.. HostTools.Read(root).Tools.Select(t => t.Name)]);
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Unit unit in catalog.Units.Values.Where(u => u.Exclusion is null && CoverageReport.Rule(u, overlay.Api)?.Status != "excluded"))
        {
            if (unit.Layer == ApiCatalog.Operation)
            {
                ids[unit.Key] = "api-read";
                ids[unit.Key + "#write"] = "api-write";
            }
            else if (unit.Layer == ApiCatalog.Action)
            { ids[unit.Key] = "action"; }
        }
        return ids;
    }

    internal static void Merge(string path, VerificationRun run, IReadOnlyDictionary<string, string> ids, string version)
        => Merge(path, [run], ids, version);

    internal static void Merge(string path, IEnumerable<VerificationRun> runs, IReadOnlyDictionary<string, string> ids, string version)
        => File.WriteAllText(path, MergeText(File.ReadAllText(path), runs, ids, version));

    internal static string MergeText(string original, IEnumerable<VerificationRun> runs, IReadOnlyDictionary<string, string> ids, string version, Action<string>? reportUnknown = null)
    {
        // Validate and merge all files before replacing the overlay; later files win for each ID.
        foreach (VerificationRun run in runs)
        { original = MergeText(original, run, ids, version, reportUnknown); }
        return original;
    }

    internal static string MergeText(string original, VerificationRun run, IReadOnlyDictionary<string, string> ids, string version, Action<string>? reportUnknown = null)
    {
        if (run.Eplan != version || !DateOnly.TryParseExact(run.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            || run.Entries.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != run.Entries.Count)
        { throw new InvalidDataException("Verification needs a matching EPLAN version, yyyy-MM-dd date and unique entry IDs."); }
        foreach (VerificationRecord entry in run.Entries)
        {
            if (!ids.TryGetValue(entry.Id, out string? kind))
            { continue; }
            if (entry.Kind != kind
                || !(entry.Outcome is "ok" or "failed" or "failed:needs_context" or "rolled_back" or "outcome_unknown" or "modal" or "timeout" || entry.Outcome.StartsWith("skipped:", StringComparison.Ordinal) && entry.Outcome.Length > 8)
                || !double.IsFinite(entry.Seconds) || entry.Seconds < 0 || entry.EplanMessages < 0)
            { throw new InvalidDataException("Invalid verification entry: " + entry.Id); }
            entry.Date = run.Date;
            entry.Eplan = run.Eplan;
        }
        var yaml = new YamlStream();
        yaml.Load(new StringReader(original));
        var mapping = (YamlMappingNode)yaml.Documents[0].RootNode;
        var key = new YamlScalarNode("verification");
        var records = new SortedDictionary<string, VerificationRecord>(StringComparer.Ordinal);
        if (mapping.Children.TryGetValue(key, out YamlNode? existing))
        {
            var parser = new YamlDotNet.Serialization.DeserializerBuilder().WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance).Build();
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            new YamlStream(new YamlDocument(existing)).Save(writer, assignAnchors: false);
            records = new(parser.Deserialize<Dictionary<string, VerificationRecord>>(writer.ToString()) ?? [], StringComparer.Ordinal);
        }
        foreach (string id in records.Keys.Concat(run.Entries.Select(e => e.Id)).Where(id => !ids.ContainsKey(id)).Distinct(StringComparer.Ordinal).ToArray())
        {
            records.Remove(id);
            if (reportUnknown is not null)
            { reportUnknown(id); }
            else
            { Console.Error.WriteLine("Dropped unknown verification ID: " + id); }
        }
        foreach (VerificationRecord entry in run.Entries)
        { if (ids.ContainsKey(entry.Id)) { records[entry.Id] = entry; } }
        string block = Render(records);
        if (existing is not null)
        {
            YamlNode actualKey = mapping.Children.Keys.Single(k => k.Equals(key));
            int afterHeader = original.IndexOf('\n', (int)actualKey.Start.Index) + 1;
            Match nextSection = afterHeader > 0 ? _section.Match(original, afterHeader) : Match.Empty;
            int end = nextSection.Success ? nextSection.Index : original.Length;
            original = original[..(int)actualKey.Start.Index] + block + original[end..];
        }
        else
        { original = original.TrimEnd('\r', '\n') + "\n\n" + block; }
        return original;
    }

    private static string Render(IEnumerable<KeyValuePair<string, VerificationRecord>> records)
    {
        string rows = string.Concat(records.Select(p => "  " + JsonSerializer.Serialize(p.Key) + ": " + JsonSerializer.Serialize(new { p.Value.Kind, p.Value.Outcome, p.Value.Changed, undo_restores = p.Value.UndoRestores, p.Value.Seconds, eplan_messages = p.Value.EplanMessages, p.Value.Date, p.Value.Eplan }, _rowJson) + "\n"));
        return rows.Length == 0 ? "verification: {}\n" : "verification:\n" + rows;
    }

    internal static string Whitelist(string root)
    {
        Overlay overlay = Overlay.Load(root, [.. HostTools.Read(root).Tools.Select(t => t.Name)]);
        Dictionary<string, string> ids = EligibleIds(root);
        return "# 批量实机验证的晋级候选数据；不改变运行时白名单。\n" + Render(overlay.Verification.Where(p => ids.TryGetValue(p.Key, out string? kind) && p.Value.Kind == kind && p.Value.Verifies).OrderBy(p => p.Key, StringComparer.Ordinal));
    }
}
