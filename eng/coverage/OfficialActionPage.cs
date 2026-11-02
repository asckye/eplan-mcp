using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EplanMcp.Coverage;

/// <summary>Reads the saved 2026 help tables; prose is transient and never serialized.</summary>
internal sealed record OfficialActionPage(Dictionary<string, string> Parameters, JsonObject[] Examples)
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    internal static MatchCollection Matches(string text, string pattern) => Regex.Matches(text, pattern, Options);
    private static string Text(string html) => WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", "", Options)).Replace('\u00a0', ' ').Trim();
    internal static string[][] Rows(string html) => [.. Matches(html, @"(?s)<tr\b[^>]*>(.*?)</tr>").Select(r =>
        Matches(r.Groups[1].Value, @"(?s)<t[dh]\b[^>]*>(.*?)</t[dh]>").Select(c => c.Groups[1].Value).ToArray()).Where(r => r.Length == 2)];
    internal static Dictionary<string, string> Index(string html) => Rows(html).SelectMany(r => Matches(r[0], "<a\\b[^>]*href=[\"']([^\"']+\\.html)[\"'][^>]*>(.*?)</a>"))
        .Where(m => Text(m.Groups[2].Value).Equals(Path.GetFileNameWithoutExtension(m.Groups[1].Value), StringComparison.OrdinalIgnoreCase))
        .ToDictionary(m => Text(m.Groups[2].Value), m => Path.GetFileName(m.Groups[1].Value), StringComparer.OrdinalIgnoreCase);

    internal static OfficialActionPage Parse(string action, string html)
    {
        Dictionary<string, string> parameters = Rows(html).Where(r => !Text(r[0]).Equals("Parameter", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(r => Text(r[0]), r => Text(r[1]), StringComparer.OrdinalIgnoreCase);
        var examples = new List<JsonObject>();
        Match section = Regex.Match(html, "id=[\"']?exampleSectionContent\\b", Options);
        if (!section.Success)
        { return new(parameters, []); }
        // Only PRE blocks in Example, never command-like fragments in descriptions or remarks.
        foreach (Match pre in Matches(html[section.Index..], @"(?s)<pre\b[^>]*>(.*?)</pre>"))
        {
            string text = Text(Regex.Replace(pre.Groups[1].Value, @"<br\s*/?>", "\n", Options));
            text = Regex.Replace(text, @"\r?\n\s*(?=/[A-Za-z_]\w*:)", " ", Options);
            foreach (Match call in Matches(text, "(?m)^[ \\t]*(?:EPLAN\\.exe /Variant:\"[^\"]*\"[ \\t]+)?" + Regex.Escape(action) + @"\b([^\r\n]*)"))
            {
                var example = new JsonObject();
                string arguments = call.Groups[1].Value.Trim();
                bool valid = true;
                while (arguments.Length > 0)
                {
                    if (arguments.StartsWith("//", StringComparison.Ordinal))
                    { break; }
                    Match arg = Regex.Match(arguments, "^/([A-Za-z_][A-Za-z0-9_]*):(?:\"([^\"]*)\"|'([^']*)'|([^\\s\"']*))(?=\\s|$)", Options);
                    if (!arg.Success || example.ContainsKey(arg.Groups[1].Value))
                    { valid = false; break; }
                    example[arg.Groups[1].Value] = arg.Groups[2].Success ? arg.Groups[2].Value : arg.Groups[3].Success ? arg.Groups[3].Value : arg.Groups[4].Value;
                    arguments = arguments[arg.Length..].TrimStart();
                }
                if (valid && !examples.Any(e => JsonNode.DeepEquals(e, example)))
                { examples.Add(example); }
            }
            // A context is attributable only when the example names this action and executes that same context.
            if (text.Contains('"' + action + '"', StringComparison.Ordinal))
            {
                foreach (var context in Matches(text, "([A-Za-z_]\\w*)\\.AddParameter\\(\"([^\"]+)\",\\s*\"([^\"]*)\"\\)").GroupBy(m => m.Groups[1].Value))
                {
                    if (Regex.IsMatch(text, @"\.Execute\(\s*" + Regex.Escape(context.Key) + @"\s*\)", Options))
                    { examples.Add(new JsonObject(context.Select(m => new KeyValuePair<string, JsonNode?>(m.Groups[2].Value, JsonValue.Create(m.Groups[3].Value))))); }
                }
            }
        }
        return new(parameters, [.. examples]);
    }

    private static string[] Values(string description, string? rowType)
    {
        Match systemFormats = Regex.Match(description, @"For parts management \(([^\r\n]+)\)", Options);
        if (systemFormats.Success && rowType?.EndsWith("SYSTEM", StringComparison.Ordinal) == true)
        { return [.. Matches(systemFormats.Groups[1].Value, "\"([^\"]+)\"").Select(m => m.Groups[1].Value)]; }
        Match branch = Regex.Match(description, "This is also used for ([^\\r\\n]+)", Options);
        if (branch.Success && rowType is not null)
        { description = branch.Value.Contains('"' + rowType + '"', StringComparison.Ordinal) ? description[(branch.Index + branch.Length)..] : description[..branch.Index]; }
        string own = Regex.Split(description, @"(?:This|These) parameters? (?:is|are|can|supports)|(?:Valid|Only applies|This only applies) (?:only )?(?:if|to)|When the parameter Type", Options)[0];
        own = Regex.Split(own, @"\r?\n\s*\r?\n", Options)[0];
        List<string> values = [.. Matches(own, @"\b([0-9]+)\s*=").Select(m => m.Groups[1].Value)];
        own = Regex.Replace(own, @"(?m)^.*\bTYPE\b.*$", "", RegexOptions.CultureInvariant);
        foreach (Match value in Matches(own, "(?m)^[ \\t]*[•]?[ \\t]*(?:\"([^\"]+)\"|((?-i:[A-Z][A-Z0-9_]*|[A-Za-z_]+[A-Z][A-Za-z0-9_]*)|[0-9]+|true|false))[ \\t]*(?:\\(default(?: value)?\\))?[ \\t]*(?:[:=;]| - |[,\\.]?[ \\t]*\\r?$)"))
        { values.Add(value.Groups[1].Success ? value.Groups[1].Value : value.Groups[2].Value); }
        foreach (Match list in Matches(own, @"(?:possible|following|allowed) values(?: are)?\s*[:=]?\s*([^\.\r\n]+)|(?:File|Output) format\s*\(([^\)]+)\)"))
        {
            string items = list.Groups[1].Success ? list.Groups[1].Value : list.Groups[2].Value;
            if (!Regex.IsMatch(items, @"\b\d+\s*[=:]", Options))
            { values.AddRange(Matches(items, "\"([^\"]+)\"").Select(m => m.Groups[1].Value)); }
            if (!items.Contains('"', StringComparison.Ordinal) && !items.Contains('=', StringComparison.Ordinal))
            { values.AddRange(items.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries).Where(s => Regex.IsMatch(s, @"\A(?:[A-Z][A-Z0-9_]*|[0-9]+)\z", Options & ~RegexOptions.IgnoreCase))); }
        }
        if (Regex.IsMatch(own, @"\b0\s*(?:or|/)\s*1\b", Options))
        { values.AddRange(["0", "1"]); }
        if (Regex.IsMatch(own, @"\btrue or false\b", Options))
        { values.AddRange(["true", "false"]); }
        values.AddRange(Matches(own, "\"([A-Za-z_][A-Za-z0-9_]*)\"\\s*-\\s*(?:export|import)\\b").Select(m => m.Groups[1].Value));
        return Regex.IsMatch(own, @"!=|<>", Options) ? [] : [.. values.Where(v => !Regex.IsMatch(v, @"\A(?:Default|Optional|Note|Increment|When)\z", Options)).Distinct(StringComparer.Ordinal)];
    }

    internal static JsonObject Facts(string description, string? rowType, string name = "")
    {
        string[] literals = Values(description, rowType);
        string first = description.Split(['\n', '.'])[0];
        string kind = literals.Length == 2 && literals.Contains("0", StringComparer.Ordinal) && literals.Contains("1", StringComparer.Ordinal) ? "flag"
            : Regex.IsMatch(first, @"\binteger\b|\bnumber of\b|\bindex\b|\bin pixels\b", Options) || literals.Length > 0 && literals.All(s => int.TryParse(s, out _)) ? "integer"
            : Regex.IsMatch(first, @"without (?:full )?path|only name", Options) ? "string"
            : !first.StartsWith("true:", StringComparison.OrdinalIgnoreCase) && (name.Equals("PROJECTNAME", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Path", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(first, @"\bpath\b|\bdirectory\b|\bfolder\b", Options) || Regex.IsMatch(description, @"\bfull path name\b|\b(?:directory|folder) name\b|\bfull project name\b|\bfull name of (?:the target )?project\b", Options)) ? "path"
            : Regex.IsMatch(description, @"\barchive name\b|\bfile ?name\b|\bname of (?:the |a |an )?(?:XML |export )?file\b|\bfile to be\b|\b(?:script|target|destination|source) file\b", Options) ? "file" : "string";
        if (kind is "path" or "file")
        { literals = []; }
        bool required = !Regex.IsMatch(description, @"\boptional\b", Options);
        if (rowType is not null && Matches(description, @"[^.\r\n]*\brequired\b[^.\r\n]*").Any(m => !m.Value.Contains("not required", StringComparison.OrdinalIgnoreCase) && m.Value.Contains('"' + rowType + '"', StringComparison.Ordinal)))
        { required = true; }
        var facts = new JsonObject { ["required"] = required, ["type"] = kind };
        if (literals.Length > 0)
        { facts["values"] = new JsonArray([.. literals.Select(v => JsonValue.Create(v))]); }
        Match direct = Regex.Match(description, "\\bDefault(?: values?)?\\s*(?:[=:]|is)\\s*(?:\"([^\"]*)\"|(-?\\d+(?:\\.\\d+)?|true|false|[A-Za-z_][A-Za-z0-9_]*))", Options);
        string? valueDefault = direct.Success ? direct.Groups[1].Success ? direct.Groups[1].Value : direct.Groups[2].Value : null;
        if (valueDefault is not null && (direct.Groups[1].Success || Regex.IsMatch(valueDefault, @"\A(?:-?\d+(?:\.\d+)?|true|false|[A-Z][A-Z0-9_]+|[A-Za-z]+[A-Z][A-Za-z0-9_]*)\z", RegexOptions.CultureInvariant)))
        { facts["default"] = valueDefault; }
        else
        {
            Match annotated = Regex.Match(description, @"(?:^|[:,\r\n•])\s*([0-9]+|[A-Za-z_][A-Za-z0-9_]*)\s*(?:[:=][^,\r\n]*?)?\s*\(default(?: value)?\)", Options);
            if (annotated.Success)
            { facts["default"] = annotated.Groups[1].Value; }
        }
        // Some tables state different format defaults for different TYPE rows.
        foreach (Match line in Matches(description, @"[^\r\n]+"))
        {
            Match typed = Regex.Match(line.Value, "\"([^\"]+)\" is the default", Options);
            if (typed.Success && rowType is not null && line.Value.Contains('"' + rowType + '"', StringComparison.Ordinal))
            { facts["default"] = typed.Groups[1].Value; }
        }
        return facts;
    }
}
