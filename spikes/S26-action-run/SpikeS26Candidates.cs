using System.Globalization;
using System.Text.RegularExpressions;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.HEServices;
using Eplan.EplApi.MasterData;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S26;

public sealed partial class SpikeS26Action
{
    private static Candidate Export(Parameters p, string id, string action, string? type, string fileParameter, string fileName, string? projectParameter = "PROJECTNAME")
    {
        string folder = SpikeS26Paths.Below(Path.Combine(p.Folder, p.Mode + "-" + id), p.Folder);
        var options = Options(fileParameter, Path.Combine(folder, fileName));
        if (type is not null) { options.Add("TYPE", type); }
        if (projectParameter is not null) { options.Add(projectParameter, p.Project); }
        return new Candidate(p.Mode + "-" + id, action, folder, options);
    }

    private static void Runner(List<Candidate> candidates, Project project, Parameters p, string[] paths)
    {
        foreach (string relative in new[] { "slash 子目录/空 格 '单引号' 中文.elc", "slash 子目录/空 格 \"双引号\" 中文.elc" })
        {
            Candidate c = Export(p, candidates.Count == 0 ? "context-valid" : "context-double-quote", "graphicallayertable", "EXPORT", "EXPORTFILE", "unused.elc");
            // Construct inside EPLAN, so Remote Client's text/Unicode handling is not part of this question (A30).
            c.Options["EXPORTFILE"] = c.Folder.Replace('\\', '/') + "/" + relative;
            c.ExpectedRelative = relative;
            c.Note = relative.Contains('"')
                ? "Diagnostic export: ASCII double quotes are illegal in Windows file names. Context equality can pass while export fails/sanitizes; this cannot prove a literal double-quote file name round trip."
                : "Forward slash is a directory separator; compare the normalized relative path and literal file name including spaces, apostrophes and Chinese.";
            candidates.Add(c);
        }

        Candidate baseline = LayerCandidate(project, p, "undo-s34-control");
        baseline.WithUndo = false;
        baseline.Note = "S34 control: SafetyPoint only. Compare with undo-bridge, which places UndoStep inside SafetyPoint. Neither commits or invokes undo.";
        candidates.Add(baseline);
        candidates.Add(LayerCandidate(project, p, "undo-bridge"));
        Candidate current = LayerCandidate(project, p, "current-project");
        current.Options.Remove("PROJECTNAME");
        current.CurrentOnly = true;
        current.Note = "Omit the documented optional PROJECTNAME on changelayer. Compare the selected layer and file fingerprints of every open scratch project. This does not validate unrelated actions with no project parameter.";
        if (paths.Length < 2) { current.Skip = "needs input: open at least two scratch project copies to test current-project isolation."; }
        candidates.Add(current);
    }

    private static void Layers(List<Candidate> candidates, Project project, Parameters p)
    {
        candidates.Add(LayerCandidate(project, p, "change-visible"));
        candidates.Add(Export(p, "export", "graphicallayertable", "EXPORT", "EXPORTFILE", "layers.elc"));
    }

    private static Candidate LayerCandidate(Project project, Parameters p, string id)
    {
        var c = new Candidate(p.Mode + "-" + id, "changelayer", Path.Combine(p.Folder, p.Mode + "-" + id), Options("PROJECTNAME", p.Project));
        try
        {
            // Project owns its table/layers: borrow them, never Dispose them (A39).
            GraphicalLayer? layer = project.LayerTable.Layers.Where(layer => layer.isValid && !layer.isLocked)
                .OrderBy(layer => layer.Name, StringComparer.Ordinal).FirstOrDefault();
            if (layer is null) { c.Skip = "needs input: no readable unlocked layer in the sample."; return c; }
            c.LayerName = layer.Name;
            c.Options["LAYER1"] = layer.Name; // [n] expands to the documented LAYER1 / VISIBLE1 (installed action XML).
            c.Options["VISIBLE1"] = layer.isVisible ? "0" : "1";
            c.Note = "Toggle one existing unlocked layer's visibility, read the same named layer back for every project, then explicitly roll back.";
        }
        catch (Exception ex) { c.Skip = "Layer discovery failed: " + Describe(ex).ToString(); }
        return c;
    }

    private static void Parts(List<Candidate> candidates, JObject report, Parameters p)
    {
        Capture(report, "configuredPartsDatabase", PartsState);
        var samples = new Dictionary<string, string>(StringComparer.Ordinal);
        // Property numbers from data-dictionary.jsonl; values come only from existing configured-library records.
        string[] ids = ["22001", "22900", "22931", "22945", "22960", "22970"];
        int index = 0;
        string? unavailable = null;
        if (!ConfiguredPartsFile(out _, out string reason)) { unavailable = reason; }
        else
        {
            Capture(report, "partsFilterDiscovery", () =>
            {
                var management = new MDPartsManagement();
                MDPartsDatabase database = management.OpenDatabase();
                try
                {
                    samples["EXPORTPARTS"] = database.Parts.Select(part => part.PartNr).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "";
                    samples["EXPORTMANUFACTURERS"] = database.Manufacturers.Select(item => item.ShortName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "";
                    samples["EXPORTCONSTRUCTIONS"] = database.Constructions.Select(item => item.Name).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "";
                    samples["EXPORTCONNECTIONPATTERNS"] = database.ConnectionPointInfos.Select(item => item.Name).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "";
                    samples["EXPORTACCESSORYLISTS"] = database.AccessoryLists.Select(item => item.Name).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "";
                    samples["EXPORTACCESSORYPLACEMENTS"] = database.AccessoryPlacements.Select(item => item.Name).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "";
                    return JObject.FromObject(samples);
                }
                finally { database.Close(); }
            });
        }
        foreach (string type in new[] { "EXPORTPARTS", "EXPORTMANUFACTURERS", "EXPORTCONSTRUCTIONS", "EXPORTCONNECTIONPATTERNS", "EXPORTACCESSORYLISTS", "EXPORTACCESSORYPLACEMENTS" })
        {
            Candidate c = Export(p, type.ToLowerInvariant() + "-no-filter", "partsmanagementapi", type, "EXPORTFILE", "parts.xml", null);
            c.Options["FORMAT"] = "XPamExportXml";
            c.PartsDatabase = true;
            c.Note = "Minimal documented export: omit PROPERTYIDn/PROPERTYVALUEn, do not invent a property filter, scheme or record name. Report failure/empty output as such. Read only the configured file database; no database path parameter is sent.";
            c.Skip = unavailable;
            candidates.Add(c);
            Candidate filtered = Export(p, type.ToLowerInvariant() + "-sample", "partsmanagementapi", type, "EXPORTFILE", "parts.xml", null);
            filtered.PartsDatabase = true;
            filtered.Options["FORMAT"] = "XPamExportXml";
            filtered.Options["PROPERTYID1"] = ids[index++];
            if (samples.TryGetValue(type, out string? name) && name.Length > 0) { filtered.Options["PROPERTYVALUE1"] = name; }
            else { filtered.Skip = unavailable ?? "needs input: no existing record name read for the documented property filter; do not invent one."; }
            filtered.Note = "PROPERTYID1 from catalog data-dictionary.jsonl (ARTICLE_PARTNR / PART_ADDRESS_SHORTNAME / PART_CONSTRUCTION_NAME / PART_TERMINAL_NAME / PART_ACCESSORYLIST_NAME / PART_ACCESSORYPLACEMENT_NAME); PROPERTYVALUE1 read from the configured database. No data objects are disposed.";
            candidates.Add(filtered);
        }
    }

    private static void Plc(List<Candidate> candidates, JObject report, Project project, Parameters p)
    {
        string[] configurations = [];
        Capture(report, "configurationDiscovery", () =>
        {
            using var service = new PlcService();
            configurations = service.GetPLCConfigurationProjects(project).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            return new JObject { ["total"] = configurations.Length, ["entries"] = new JArray(configurations.Take(12)), ["tested"] = new JArray(configurations.Take(2)) };
        });
        IEnumerable<string?> configurationCandidates = configurations.Length == 0 ? new string?[] { null } : configurations.Take(2).Select(name => (string?)name);
        foreach (string? configuration in configurationCandidates)
        {
            int index = 0;
            foreach (string converter in new[] { "PlcDcXMLExchangerUniversal", "PlcDcAMLExchangerGeneral", "PlcDcAMLExchangerGeneral" })
            {
                Candidate c = Export(p, "bus-" + candidates.Count.ToString("D2", CultureInfo.InvariantCulture), "plcservice", "BUSDATAEXPORT", "DESTINATIONFILE", converter == "PlcDcXMLExchangerUniversal" ? "bus.pbf" : "bus.aml");
                c.Options["OVERWRITE"] = "0";
                c.Options["LANGUAGE"] = "de_DE";
                c.Options["CONVERTERID"] = converter;
                if (index++ == 2) { c.Options["FORMAT"] = "AutomationML AR APC V1.4.0"; }
                if (configuration is null) { c.Skip = "needs input: no readable PLC configuration project; do not export with an empty configuration."; }
                else { c.Options["CONFIGURATIONPROJECT"] = configuration; }
                c.Note = "Same converters/format as S34; export only, never BUSDATAIMPORT.";
                candidates.Add(c);
            }
        }

        var targets = new List<Dictionary<string, string>>();
        Capture(report, "addressTargetDiscovery", () => DiscoverAddressTargets(project, configurations, targets));
        if (targets.Count == 0)
        {
            Candidate c = Export(p, "address-needs-input", "plcservice", "EXPORTADDRESSOVERVIEW", "DESTINATIONFILE", "addresses.csv");
            c.Skip = "needs input: no unambiguous configuration project, station and CPU tuple read from the sample's PLC properties (A25).";
            candidates.Add(c);
        }
        foreach (Dictionary<string, string> target in targets.Take(2))
        {
            Candidate c = Export(p, "address-" + candidates.Count.ToString("D2", CultureInfo.InvariantCulture), "plcservice", "EXPORTADDRESSOVERVIEW", "DESTINATIONFILE", "addresses.csv");
            foreach (KeyValuePair<string, string> pair in target) { c.Options.Add(pair.Key, pair.Value); }
            candidates.Add(c); // OVERWRITE is not documented for EXPORTADDRESSOVERVIEW: deliberately absent.
        }
    }

    private static JObject DiscoverAddressTargets(Project project, string[] configurations, List<Dictionary<string, string>> targets)
    {
        using var finder = new DMObjectsFinder(project);
        var rows = new JArray();
        foreach (var plc in finder.GetPLCs(null))
        {
            var row = new JObject { ["id"] = plc.ObjectIdentifier };
            rows.Add(row);
            try
            {
                // A36: acquire this PLC's property list ONCE; read all fields, dispose values and the list once.
                using var properties = plc.Properties;
                string configuration = PropertyText(row, "configuration", () => properties.FUNC_PLCCONFIGURATIONPROJECT);
                string station = PropertyText(row, "station", () => properties.FUNC_PLCSTATIONNAME);
                string cpus = PropertyText(row, "cpus", () => properties.FUNC_PLCCPU);
                if (configuration.Length == 0 || station.Length == 0 || !configurations.Contains(configuration, StringComparer.Ordinal)) { continue; }
                foreach (string cpu in cpus.Split(';').Where(cpu => !string.IsNullOrWhiteSpace(cpu)))
                {
                    // XML: complete CPU names are [configuration].[station].[CPU]. Never split on arbitrary dots in names.
                    string prefix = configuration + "." + station + ".";
                    if (!cpu.StartsWith(prefix, StringComparison.Ordinal) || cpu.Length <= prefix.Length) { continue; }
                    var target = Options("CONFIGURATIONPROJECT", configuration, "PLCSTATION", station, "PLCCPU", cpu.Substring(prefix.Length));
                    if (!targets.Any(old => old.OrderBy(pair => pair.Key, StringComparer.Ordinal).SequenceEqual(target.OrderBy(pair => pair.Key, StringComparer.Ordinal)))) { targets.Add(target); }
                }
            }
            catch (Exception ex) { row["error"] = Describe(ex); }
        }

        return new JObject { ["source"] = "catalog/installed XML: FUNC_PLCCONFIGURATIONPROJECT=20161, FUNC_PLCSTATIONNAME=20408, FUNC_PLCCPU=20433", ["plcs"] = rows, ["tuples"] = JArray.FromObject(targets), ["testedMaximum"] = 2 };
    }

    private static string PropertyText(JObject row, string name, Func<PropertyValue> read)
    {
        try
        {
            using PropertyValue value = read();
            string text = value.IsEmpty ? "" : value.ToString();
            row[name] = text;
            return text;
        }
        catch (Exception ex) { row[name] = new JObject { ["error"] = Describe(ex) }; return ""; }
    }

    private static void External(List<Candidate> candidates, JObject report, Parameters p)
    {
        foreach (bool articles in new[] { false, true })
        {
            string action = articles ? "XMExportDCArticleDataAction" : "XMActionDCCommonExport";
            Candidate c = Export(p, articles ? "articles" : "project", action, null, "DESTINATION", "external", articles ? null : "PROJECTNAME");
            c.PartsDatabase = articles;
            c.Options["EXECUTIONMODE"] = "0";
            c.Options["IMMEDIATEIMPORT"] = "0";
            c.Options["COMPLETEPROJECT"] = "1";
            string node = articles ? p.ArticleSchemeNode : p.CommonSchemeNode;
            var discovery = new JObject { ["node"] = node, ["source"] = "Engineer-supplied verified settings node; no guessed settings paths or scheme names." };
            report[articles ? "articleSchemeDiscovery" : "commonSchemeDiscovery"] = discovery;
            string? name = null;
            Capture(discovery, "read", () =>
            {
                if (!Regex.IsMatch(node, @"\AUSER\.[A-Za-z0-9_.]+\z", RegexOptions.CultureInvariant)) { throw new ArgumentException("No verified USER scheme node supplied."); }
                using var scheme = new SchemeSetting();
                scheme.Init(node);
                var names = new List<string>();
                int count = scheme.GetCount();
                for (int i = 0; i < Math.Min(200, count); i++)
                {
                    scheme.SetScheme(i); // Reader only; never SetLastUsed.
                    string found = scheme.GetName();
                    if (!string.IsNullOrWhiteSpace(found) && found != "InternalRefDefScheme") { names.Add(found); }
                }
                name = names.OrderBy(found => found, StringComparer.Ordinal).FirstOrDefault();
                return new JObject { ["count"] = count, ["entries"] = new JArray(names), ["unscanned"] = Math.Max(0, count - 200), ["selected"] = name };
            });
            if (name is null) { c.Skip = "needs a scheme: no existing name could be read from a verified scheme node. Missing CONFIGSCHEME opens a dialog (api-actions 7.3)."; }
            else { c.Options["CONFIGSCHEME"] = name; }
            if (articles && !ConfiguredPartsFile(out _, out string reason)) { c.Skip = reason; }
            c.Note = "Destination is fixed inside the new candidate folder; output format/extension follows the existing scheme. No DATABASEID, language, edit mode, immediate import or guessed extra parameter.";
            candidates.Add(c);
        }
    }

    private static Dictionary<string, string> Options(params string[] pairs)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < pairs.Length; i += 2) { result.Add(pairs[i], pairs[i + 1]); }
        return result;
    }
}
