using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Exports;

namespace EplanMcp.Host.Tools;

/// <summary>Shipped rules only; disabled entries belong to this Host process (ADR-0021).</summary>
internal sealed class ActionRunCatalog
{
    public static ActionRunCatalog Process { get; } = new();
    public ConcurrentDictionary<string, byte> Disabled { get; } = new(StringComparer.Ordinal);
    public static JsonObject Document { get; } = Load();
    public static IReadOnlyDictionary<string, JsonObject> Entries { get; } = Document["entries"]!.AsArray().ToDictionary(e => e!["entry"]!.GetValue<string>(), e => e!.AsObject(), StringComparer.Ordinal);
    private static JsonObject Load()
    {
        using Stream stream = typeof(ActionRunArgs).Assembly.GetManifestResourceStream("EplanMcp.Contracts.ActionRunWhitelist.json")!;
        return JsonNode.Parse(stream)!.AsObject();
    }
    public static BridgeCallException Denied(string message = "Not in the action whitelist.") => new(new BridgeError { Reason = ErrorReason.PolicyDenied, Message = message });
    internal static string Text(JsonNode entry, string key) => entry[key]!.GetValue<string>();
    internal static string Extension(JsonObject entry, ActionRunArgs args) => args.Entry == "backup/MASTERDATA"
        ? args.Parameters.Single(p => p.Name == "MDTYPE").Value == "LANGUAGES" ? ".zw7" : ".zw6"
        : args.Entry == "plcservice/BUSDATAEXPORT" ? args.Parameters.Single(p => p.Name == "CONVERTERID").Value == "PlcDcXMLExchangerUniversal" ? ".pbf" : ".aml" : Text(entry, "extension");
    public (JsonObject Entry, ActionRunArgs Args) Check(string name, string? project, JsonElement arguments)
    {
        name ??= string.Empty;
        if (!Entries.TryGetValue(name, out JsonObject? entry))
        {
            throw Denied();
        }
        if (Disabled.ContainsKey(name))
        {
            throw Denied("This entry is disabled in this Host process; ask the engineer to reverify it.");
        }
        var problems = new List<string>();
        bool projectEntry = Text(entry, "scope") == "project";
        if (projectEntry ? string.IsNullOrWhiteSpace(project) : project is not null)
        {
            problems.Add("Supply a non-empty project only for project entries.");
        }
        var args = new ActionRunArgs { Entry = name, Project = project };
        JsonProperty[] given = arguments.ValueKind == JsonValueKind.Object ? [.. arguments.EnumerateObject()] : [];
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            problems.Add("arguments must be an object.");
        }
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string expanded, JsonNode? rule) in entry["parameters"]!.AsObject())
        {
            bool list = expanded.EndsWith('1');
            string key = list ? expanded[..^1] : expanded.TrimEnd('?');
            allowed.Add(key);
            JsonProperty[] matches = [.. given.Where(p => p.Name == key)];
            if (matches.Length == 0 && expanded.EndsWith('?'))
            {
                continue;
            }
            JsonElement value = matches.Length == 1 ? matches[0].Value : default;
            string? literal = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            string kind = rule!.GetValue<string>();
            bool valid = literal is { Length: >= 1 and <= 100 } && !string.IsNullOrWhiteSpace(literal) && !literal.StartsWith('/') && !literal.Any(c => char.IsControl(c) || c is '\'' or '"')
                && (kind is "text" or "file" || kind == "archive" && Regex.IsMatch(literal, "^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant) || kind.Split('|').Contains(literal, StringComparer.Ordinal))
                && (kind != "file" || ExportFileName.Problem(literal) is null) && (key != "PROPERTYVALUE" || literal.IndexOfAny(['*', '?']) < 0);
            if (valid && kind == "file")
            {
                JsonElement converter = given.FirstOrDefault(p => p.Name == "CONVERTERID").Value;
                string extension = args.Entry == "plcservice/BUSDATAEXPORT" && converter.ValueKind == JsonValueKind.String && converter.GetString() == "PlcDcAMLExchangerGeneral" ? ".aml" : Text(entry, "extension").Split('|')[0];
                valid = !Path.HasExtension(literal) || literal!.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
                literal = literal!.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? literal : literal + extension;
                valid &= literal.Length > extension.Length && literal.Length <= 100;
            }
            if (!valid)
            {
                problems.Add(key + " is missing, duplicated or invalid.");
            }
            else
            {
                args.Parameters.Add(new() { Name = expanded.TrimEnd('?'), Value = literal! });
            }
        }
        problems.AddRange(given.Where(p => !allowed.Contains(p.Name)).Select(p => p.Name + " is not allowed for this entry."));
        if (args.Entry == "plcservice/BUSDATAEXPORT" && args.Parameters.Any(p => p.Name == "FORMAT") && !args.Parameters.Any(p => p.Name == "CONVERTERID" && p.Value == "PlcDcAMLExchangerGeneral"))
        {
            problems.Add("FORMAT requires PlcDcAMLExchangerGeneral.");
        }
        if (problems.Count > 0)
        {
            BridgeError error = ToolResults.InvalidArgumentError(problems);
            error.Metadata!["allowedArguments"] = string.Join(", ", allowed);
            error.Metadata["hint"] = "Call eplan_tool_usage with tool: \"eplan_action_run\", topic: \"" + name + "\".";
            error.Message += " Allowed arguments: " + error.Metadata["allowedArguments"] + ". " + error.Metadata["hint"];
            throw new BridgeCallException(error);
        }
        return (entry, args);
    }
}
