using System.Text.RegularExpressions;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Bridge's independent whitelist check (SEC-33). Never reads a configurable whitelist.</summary>
internal sealed class ActionRunRequest(JObject entry, OpenProject? project, ActionRunData data)
{
    public JObject Entry { get; } = entry;
    public OpenProject? Project { get; } = project;
    public ActionRunData Data { get; } = data;
    public string StateFingerprint() => Fingerprint.Of([Entry["entry"]!.ToString(), Data.Target, .. Data.Before.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => JsonConvert.SerializeObject(p)),
        .. Data.Parameters.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => JsonConvert.SerializeObject(p))]);

    internal static JObject Document()
    {
        using Stream stream = typeof(ActionRunArgs).Assembly.GetManifestResourceStream("EplanMcp.Contracts.ActionRunWhitelist.json")
            ?? throw new InvalidOperationException("Missing embedded action whitelist.");
        using var reader = new StreamReader(stream);
        return JObject.Parse(reader.ReadToEnd());
    }

    public static ActionRunRequest Check(ActionRunArgs args, int major, IProjectCatalog catalog, IActionRunner runner, string? tier = null)
    {
        JObject document = Document();
        JObject entry = document["entries"]!.OfType<JObject>().SingleOrDefault(e => (string?)e["entry"] == args.Entry)
            ?? throw new BridgeErrorException(ErrorReason.PolicyDenied, "Not in the action whitelist.");
        if (major != (int)document["verification"]!["major"]! || (tier is not null && (string?)entry["tier"] != tier))
        {
            throw new BridgeErrorException(ErrorReason.PolicyDenied, "This entry is not verified for this EPLAN version or execution tier.");
        }

        var violations = new List<string>();
        bool projectEntry = (string?)entry["scope"] == "project";
        if (projectEntry == string.IsNullOrWhiteSpace(args.Project))
        { violations.Add("Supply project only for a project entry; project entries require it."); }
        if ((args.Parameters ?? []).Any(p => p is null || p.Name is null) || (args.Parameters ?? []).GroupBy(p => p.Name, StringComparer.Ordinal).Any(g => g.Count() > 1))
        { throw WriteArguments.Invalid(["Parameter names must be unique and non-null."]); }
        var parameters = (args.Parameters ?? []).ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
        var rules = (JObject)entry["parameters"]!;
        foreach (JProperty rule in rules.Properties())
        {
            string key = rule.Name.TrimEnd('?');
            if (!parameters.TryGetValue(key, out string? value))
            {
                if (!rule.Name.EndsWith("?", StringComparison.Ordinal))
                { violations.Add(key + " is required."); }
                continue;
            }

            string kind = (string)rule.Value!;
            bool valid = Text(value) && (kind switch
            {
                "text" => key != "PROPERTYVALUE1" || value.IndexOfAny(['*', '?']) < 0,
                "file" => FileName(value),
                "archive" => Regex.IsMatch(value, @"\A[A-Za-z0-9_-]+\z", RegexOptions.CultureInvariant),
                _ => kind.Split('|').Contains(value, StringComparer.Ordinal),
            });
            if (!valid)
            { violations.Add(key + " has an invalid value."); }
        }

        violations.AddRange(parameters.Keys.Where(k => rules[k] is null && rules[k + "?"] is null).Select(k => k + " is not a variable parameter of this entry."));
        parameters.TryGetValue("CONVERTERID", out string? converter);
        if (args.Entry == "plcservice/BUSDATAEXPORT" && parameters.ContainsKey("FORMAT") && converter != "PlcDcAMLExchangerGeneral")
        {
            violations.Add("FORMAT requires PlcDcAMLExchangerGeneral.");
        }
        string extension = args.Entry == "backup/MASTERDATA" ? string.Empty
            : args.Entry == "plcservice/BUSDATAEXPORT" ? (converter == "PlcDcXMLExchangerUniversal" ? ".pbf" : ".aml") : (string?)entry["extension"] ?? string.Empty;
        if (entry["output"] is { } output && parameters.TryGetValue((string)output!, out string? file) && extension.Length > 0
            && (file is null || !file.EndsWith(extension, StringComparison.OrdinalIgnoreCase) || file.Length <= extension.Length))
        { violations.Add("Output file must include the entry's extension."); }
        if (violations.Count > 0)
        { throw WriteArguments.Invalid(violations); }
        OpenProject? project = !projectEntry ? null : (string?)entry["tier"] == "write"
            ? WriteArguments.Writable(catalog, args.Project) : ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        foreach (JProperty fixedValue in ((JObject)entry["fixed"]!).Properties())
        { parameters.Add(fixedValue.Name, (string)fixedValue.Value!); }
        if (project is not null)
        { parameters.Add((string)entry["projectParameter"]!, project.Path); }
        ActionRunData data = runner.Inspect(args.Entry, project?.Path, parameters);
        return new ActionRunRequest(entry, project, data);
    }

    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value!.Length <= 100
        && !value.StartsWith("/", StringComparison.Ordinal) && !value.Any(c => char.IsControl(c) || c is '\'' or '"');
    private static bool FileName(string value) => value.IndexOfAny("\\/:<>\"|?*".ToCharArray()) < 0 && value.IndexOf("..", StringComparison.Ordinal) < 0
        && !value.EndsWith(".", StringComparison.Ordinal) && !value.EndsWith(" ", StringComparison.Ordinal)
        && !Regex.IsMatch(value, @"\A(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³]) *(?:\.|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
