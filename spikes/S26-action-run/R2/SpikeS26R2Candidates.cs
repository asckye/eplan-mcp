using System.Text.RegularExpressions;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Topology;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S26;

public sealed partial class SpikeS26Action
{
    private static Candidate R2Write(Parameters p, int row, string action, string? type = null)
    {
        var c = new Candidate(p.Mode + "-" + (type ?? action).ToLowerInvariant(), action,
            Path.Combine(p.Folder, p.Mode + "-" + (type ?? action).ToLowerInvariant()), Options("PROJECTNAME", p.Project)) { SourceRow = row };
        if (type is not null) { c.Options.Add("TYPE", type); }
        return c;
    }

    private static void R2Candidates(List<Candidate> candidates, JObject report, Project project, Parameters p)
    {
        using var finder = new DMObjectsFinder(project);
        switch (p.Mode)
        {
            case "r2-sync":
                string[] types = ["MULTILINE", "SINGLELINE", "OVERVIEW"];
                for (int i = 0; i < types.Length; i++)
                {
                    Candidate c = R2Write(p, 118 + i, "synchronize", types[i]);
                    if (finder.GetFunctions(null).Length == 0) { c.Skip = "needs input: functions with differing data in multiple representations."; }
                    c.Note = "Project representations only; never SYSTEMPARTSTOPROJECT or PARTSTOSYSTEM. No observed semantic effect means needs_effect_sample.";
                    candidates.Add(c);
                }
                break;
            case "r2-type":
                Candidate change = R2Write(p, 117, "SwitchProjectType");
                bool macro = project.TypeOfProject == Project.ProjectType.Macro;
                change.Options["PROJECTTYPE"] = macro ? "1" : "2";
                change.ExpectedType = macro ? "Schematic" : "Macro";
                change.Note = "Isolated scratch copy only. Installed XML: TypeOfProject may reopen exclusively; rollback, object validity and project set must be rechecked by path. Never repair by switching back.";
                candidates.Add(change);
                break;
            case "r2-macros":
                Candidate prepare = R2Write(p, 76, "preparemacros");
                prepare.Options["GroupMacroBoxes"] = "1";
                prepare.Options["GroupPageMacros"] = "0";
                prepare.Options["SetHandleActive"] = "0";
                if (project.TypeOfProject != Project.ProjectType.Macro || finder.GetAll<MacroBox>(true).Length == 0)
                { prepare.Skip = "needs input: an already open scratch macro project with ungrouped macro boxes; no automatic type conversion or macro file generation."; }
                prepare.Note = "Only group existing macro boxes. No generatemacros, directories, master data or handle activation. Compare macro-box group membership and groups across rollback.";
                candidates.Add(prepare);
                break;
            case "r2-filling":
                Candidate filling = R2Write(p, 133, "UpdateSegmentsFilling");
                if (finder.GetAll<Segment>(true).Length == 0) { filling.Skip = "needs input: scratch topology with routed segments and stale fill values."; }
                filling.Note = "Read CABLINGSEGMENT_FILLING (#20332) on every segment before/after/rollback; do not generate routes or acquire a licence.";
                candidates.Add(filling);
                break;
            case "r2-exports": R2Exports(candidates, report, project, p); break;
            default: throw new ArgumentException("Unknown r2 mode.");
        }
    }

    private static void R2Exports(List<Candidate> candidates, JObject report, Project project, Parameters p)
    {
        Candidate common = Export(p, "common", "XMActionDCCommonExport", null, "DESTINATION", "external.xml");
        common.SourceRow = 155;
        common.Options["EXECUTIONMODE"] = "0";
        common.Options["IMMEDIATEIMPORT"] = "0";
        common.Options["COMPLETEPROJECT"] = "1";
        string schemeName = p.R2.Value<string>("commonScheme") ?? "";
        string token = p.R2.Value<string>("exportToken") ?? "";
        var discovery = new JObject { ["node"] = p.CommonSchemeNode, ["requestedScheme"] = schemeName };
        report["r2CommonScheme"] = discovery;
        Capture(discovery, "read", () =>
        {
            if (!Regex.IsMatch(p.CommonSchemeNode, @"\AUSER\.[A-Za-z0-9_.]+\z", RegexOptions.CultureInvariant) ||
                string.IsNullOrWhiteSpace(schemeName) || schemeName == "InternalRefDefScheme")
            { throw new ArgumentException("Supply a verified USER node and an existing XML export scheme by exact internal name."); }
            using var scheme = new SchemeSetting();
            scheme.Init(p.CommonSchemeNode);
            if (!scheme.CheckIfSchemeExists(schemeName)) { throw new ArgumentException("The requested scheme does not exist at this node."); }
            return new JObject { ["exists"] = true, ["lastUsed"] = scheme.GetLastUsed(), ["format"] = "XML required; engineer must verify scheme format before execution." };
        });
        using var finder = new DMObjectsFinder(project);
        bool knownToken = token.Length > 0 && (finder.GetPages(null).Any(page => page.Name == token) || finder.GetFunctions(null).Any(function => function.Name == token));
        if (discovery["read"]?.Value<bool?>("exists") != true || !knownToken)
        { common.Skip = "needs input: verified XML scheme node/name and --export-token matching an existing full page/function name. Never export using defaults."; }
        common.Options["CONFIGSCHEME"] = schemeName;
        common.ExpectedToken = token;
        common.Note = "Pure project export only; no DATABASEID, editing or immediate import. Require one exact external.xml with the sample token, unchanged project readback and fingerprints. No library is opened.";
        candidates.Add(common);

        var targets = new List<Dictionary<string, string>>();
        Capture(report, "r2AddressTargets", () =>
        {
            using var plc = new PlcService();
            return DiscoverAddressTargets(project, plc.GetPLCConfigurationProjects(project), targets);
        });
        string address = p.R2.Value<string>("addressToken") ?? "";
        var addresses = new HashSet<string>(StringComparer.Ordinal);
        Capture(report, "r2AddressSample", () =>
        {
            foreach (var terminal in finder.GetTerminals(null).Where(terminal => terminal.PLC is not null))
            {
                using var properties = terminal.Properties;
                using var value = properties.FUNC_PLCADDRESS;
                if (!value.IsEmpty) { addresses.Add(value.ToString()); }
            }
            return new JObject { ["knownAddress"] = address.Length > 0 && addresses.Contains(address), ["tupleCount"] = targets.Count };
        });
        Candidate plcExport = Export(p, "address", "plcservice", "EXPORTADDRESSOVERVIEW", "DESTINATIONFILE", "addresses.csv");
        plcExport.SourceRow = 75;
        plcExport.ExpectedToken = address;
        if (targets.Count != 1 || report["r2AddressSample"]?.Value<bool?>("knownAddress") != true)
        { plcExport.Skip = "needs input: exactly one API-confirmed configuration/station/CPU tuple and a non-empty --address-token from existing PLC terminals. Empty tuples or header-only output do not pass."; }
        else { foreach (var pair in targets[0]) { plcExport.Options.Add(pair.Key, pair.Value); } }
        plcExport.Note = "Use only the complete tuple read by A40's discovery. No guessed CPU, OVERWRITE, FORMAT, import or bus export. Require exact addresses.csv containing the known address.";
        candidates.Add(plcExport);
    }
}
