using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using EplanMcp.Host.Addin;

namespace EplanMcp.Host.Tools;

/// <summary>Reads installation data per call; no local installation content is persisted or shared between calls.</summary>
internal sealed class ActionCatalogReader(Func<(string? Bin, string? Version)> installation)
{
    public ActionCatalogReader() : this(FindInstallation) { }

    private static readonly Lazy<ActionCatalogItem[]> _officialItems = new(ReadOfficial);

    internal static ActionCatalogItem[] Official() => _officialItems.Value;

    private static ActionCatalogItem[] ReadOfficial()
    {
        using Stream stream = typeof(ActionCatalogReader).Assembly.GetManifestResourceStream("EplanMcp.Actions")!;
        using JsonDocument document = JsonDocument.Parse(stream);
        return [.. document.RootElement.EnumerateArray().Select(row => Create(row.GetProperty("action").GetString()!, "official",
            row.GetProperty("type").GetString(), null, null, [.. row.GetProperty("parameters").EnumerateArray().Select(p => p.Clone())],
            row.TryGetProperty("reason", out var reason) ? reason.GetString() : null,
            "docs/eplan/api-actions.md; row " + row.GetProperty("row").GetRawText() + "; " + row.GetProperty("docUrl").GetString()))];
    }

    internal static ActionCatalogItem Create(string name, string source, string? type, string? location, string? text,
        JsonElement[] parameters, string? excluded, string reference)
    {
        var rule = ActionRunCatalog.Entries.Values.FirstOrDefault(r => ActionRunCatalog.Text(r, "action") == name && r["fixed"]?["TYPE"]?.GetValue<string>() == type);
        return new(name, source, type, ResultShaper.CleanText(location), ResultShaper.CleanText(text), parameters,
            rule is not null ? "A" : excluded is not null ? "excluded" : "C-candidate", excluded,
            rule?["entry"]?.GetValue<string>(), rule is null ? null : "docs/adr/0021-action-run-whitelist.md; docs/eplan/api-notes.md; "
                + ActionRunCatalog.Document["verification"]!["version"]!.GetValue<string>() + "; "
                + ActionRunCatalog.Document["verification"]!["source"]!.GetValue<string>() + "; " + ActionRunCatalog.Text(rule, "effects"), ResultShaper.CleanText(reference)!);
    }

    public async Task<ActionCatalogData> ReadAsync(CancellationToken cancellationToken)
    {
        var items = new List<ActionCatalogItem>(Official());
        var gaps = new List<string>();
        string? version = null;
        try
        {
            (string? bin, version) = installation();
            if (bin is null || !Directory.Exists(bin))
            {
                gaps.Add("No unambiguous local EPLAN Bin installation was found; UI actions are unavailable. Check the installation with doctor.");
            }
            else
            {
                bin = Path.TrimEndingDirectorySeparator(bin);
                if (version != ActionRunCatalog.Document["verification"]!["version"]!.GetValue<string>())
                {
                    gaps.Add("Installation version differs from the verified catalog build or is unknown; UI structure and whitelist behavior are unverified for this version.");
                }
                var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
                string[] files = [.. new[] { bin, Path.Combine(Path.GetDirectoryName(bin)!, "Cfg") }.Where(Directory.Exists)
                    .SelectMany(d => Directory.EnumerateFiles(d, "*.xml", options)).Where(f => IsDefinition(Path.GetFileName(f))).Order(StringComparer.Ordinal).Take(65)];
                if (files.Length == 0)
                { gaps.Add("MFTools/ribbon/menu definitions were not found; UI actions are unavailable."); }
                if (files.Length > 64)
                { gaps.Add("Definition file limit reached; remaining files were not read."); }
                foreach (string file in files.Take(64))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        using var stream = File.OpenRead(file);
                        using var xml = XmlReader.Create(stream, new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 });
                        var doc = await XDocument.LoadAsync(xml, LoadOptions.None, cancellationToken).ConfigureAwait(false);
                        ActionCatalogItem[] parsed = Parse(doc, Path.GetRelativePath(Path.GetDirectoryName(bin)!, file).Replace('\\', '/'), cancellationToken);
                        items.AddRange(parsed);
                        if (parsed.Length == 0)
                        { gaps.Add(Path.GetFileName(file) + ": no recognized action definitions; unknown structure was skipped."); }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
                    {
                        gaps.Add(Path.GetFileName(file) + ": definition could not be read; check access, XML format and size.");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            gaps.Add("Installation definitions could not be enumerated; check installation access with doctor.");
        }
        if (items.Any(i => i.Source == "ui" && (i.Location is null || i.UiText is null)))
        {
            gaps.Add("Some UI labels or menu/ribbon locations are unavailable; resource and category IDs are not translated or treated as menu locations.");
        }
        return new([.. items.DistinctBy(i => JsonSerializer.Serialize(i, HostJson.Options)).OrderBy(i => i.Name, StringComparer.Ordinal)
            .ThenBy(i => i.Source, StringComparer.Ordinal).ThenBy(i => i.Type, StringComparer.Ordinal).ThenBy(i => i.Reference, StringComparer.Ordinal)], ResultShaper.CleanText(version),
            [.. gaps.Distinct(StringComparer.Ordinal).Take(8).Select(g => ResultShaper.CleanText(g)!), .. gaps.Count > 8 ? Enumerable.Repeat("Additional definition gaps omitted; the UI catalog is incomplete.", 1) : []]);
    }

    private static bool IsDefinition(string name) => name.Equals("MFTools.xml", StringComparison.OrdinalIgnoreCase)
        || name.Contains("ribbon", StringComparison.OrdinalIgnoreCase) || name.Contains("menu", StringComparison.OrdinalIgnoreCase);

    internal static ActionCatalogItem[] Parse(XDocument document, string reference, CancellationToken cancellationToken = default)
    {
        var rows = new List<ActionCatalogItem>();
        ActionCatalogItem[] official = Official();
        foreach (XElement element in document.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? command = null;
            if (element.Name.LocalName == "Val" && element.Parent?.Name.LocalName == "Setting"
                && element.Ancestors().Any(a => a.Name.LocalName == "LEV1" && (string?)a.Attribute("name") is "ActionCategory" or "UsedActions" or "SampleActions"))
            {
                command = element.Value.Split(';')[0];
            }
            if (element.Name.LocalName is "Button" or "MenuItem" or "Command")
            { command = (string?)element.Attribute("action"); }
            if (command is null || command.Length > 4096)
            { continue; }
            Match name = Regex.Match(command, @"^\s*([A-Za-z_][A-Za-z0-9_.]*)\s*(?=/|$)", RegexOptions.CultureInvariant);
            if (!name.Success || name.Groups[1].Value.Length > 500)
            { continue; }
            Match[] parameters = [.. Regex.Matches(command, @"/([A-Za-z_][A-Za-z0-9_]*):([^/\r\n]*)", RegexOptions.CultureInvariant).Cast<Match>()];
            string? type = parameters.FirstOrDefault(p => p.Groups[1].Value.Equals("TYPE", StringComparison.OrdinalIgnoreCase))?.Groups[2].Value.Trim().Trim('"');
            string? location = string.Join(" / ", element.Ancestors().Reverse().Where(a => a.Name.LocalName is "Tab" or "Group" or "Menu").Select(a => (string?)a.Attribute("text")).OfType<string>());
            string action = name.Groups[1].Value;
            ActionCatalogItem? known = official.FirstOrDefault(i => i.Name == action && i.Type == type);
            rows.Add(Create(action, "ui", type, location.Length == 0 ? null : location, (string?)element.Attribute("text"),
                [.. (known?.Parameters ?? []).Concat(parameters.Select(p => JsonSerializer.SerializeToElement(new { name = p.Groups[1].Value })))
                    .DistinctBy(p => p.GetProperty("name").GetString(), StringComparer.Ordinal)], known?.ExcludedReason,
                reference + ":" + ((string?)element.Parent?.Attribute("name") ?? element.Name.LocalName)));
        }
        return [.. rows];
    }

    private static (string?, string?) FindInstallation()
    {
        var files = EplanInstallation.Executables(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        return files.Count == 1 ? (Path.GetDirectoryName(files[0]), FileVersionInfo.GetVersionInfo(files[0]).FileVersion) : (null, null);
    }
}
