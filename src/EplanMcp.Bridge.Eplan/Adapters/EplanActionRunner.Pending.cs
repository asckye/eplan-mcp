using System.Text.RegularExpressions;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Topology;
using EplanMcp.Bridge.Core;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanActionRunner
{
    // PENDING VERIFICATION: unreachable until an entry is promoted from pendingVerification.
    // Installed 2026 XML/API catalog supplies members; S26 r2 has NOT been run. No change to the active twelve rules.
    private static void InspectPending(string entry, Project project, IReadOnlyDictionary<string, string> parameters, ActionRunData data)
    {
        if (entry == "SwitchProjectType")
        { data.Before["project:type"] = project.TypeOfProject.ToString(); }
        else if (entry == "preparemacros")
        {
            if (project.TypeOfProject != Project.ProjectType.Macro)
            { throw Invalid("This entry requires an existing macro project."); }
            using var finder = new DMObjectsFinder(project);
            foreach (MacroBox box in finder.GetAll<MacroBox>(true))
            { data.Before["macro:group:" + box.ObjectIdentifier] = box.Group?.ObjectIdentifier.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"; }
        }
        else if (entry == "UpdateSegmentsFilling")
        {
            using var finder = new DMObjectsFinder(project);
            foreach (Segment segment in finder.GetAll<Segment>(true))
            {
                using var properties = segment.Properties;
                using var value = properties.CABLINGSEGMENT_FILLING;
                data.Before["segment:fill:" + segment.ObjectIdentifier] = value.IsEmpty ? "<unset>" : value.ToString();
            }
        }
        else if (entry.StartsWith("synchronize/", StringComparison.Ordinal))
        {
            using var finder = new DMObjectsFinder(project);
            foreach (Function function in finder.GetFunctions(null))
            {
                using var properties = function.Properties;
                using var value = properties.FUNC_TEXT;
                data.Before["function:text:" + function.ObjectIdentifier] = value.IsEmpty ? "<unset>" : value.ToString();
            }
        }
        else if (entry == "plcservice/EXPORTADDRESSOVERVIEW")
        {
            using var finder = new DMObjectsFinder(project);
            string configuration = parameters["CONFIGURATIONPROJECT"];
            string station = parameters["PLCSTATION"];
            string cpu = configuration + "." + station + "." + parameters["PLCCPU"];
            bool found = finder.GetPLCs(null).Any(plc =>
            {
                using var properties = plc.Properties;
                using var configured = properties.FUNC_PLCCONFIGURATIONPROJECT;
                using var located = properties.FUNC_PLCSTATIONNAME;
                using var cpus = properties.FUNC_PLCCPU;
                return !configured.IsEmpty && !located.IsEmpty && !cpus.IsEmpty && configured.ToString() == configuration && located.ToString() == station
                    && cpus.ToString().Split(';').Contains(cpu, StringComparer.Ordinal);
            });
            if (!found) { throw Invalid("The configuration project, station and CPU must be an existing complete PLC tuple."); }
        }
        else if (entry == "XMActionDCCommonExport")
        {
            using Stream stream = typeof(ActionRunArgs).Assembly.GetManifestResourceStream("EplanMcp.Contracts.ActionRunWhitelist.json")!;
            using var reader = new StreamReader(stream);
            // Only promoted rules may supply this node; neither tool input nor configuration can set it.
            JToken? rule = JObject.Parse(reader.ReadToEnd())["entries"]!.SingleOrDefault(item => (string?)item["entry"] == entry);
            string node = (string?)rule?["schemeNode"] ?? string.Empty;
            if (!Regex.IsMatch(node, @"\AUSER\.[A-Za-z0-9_.]+\z", RegexOptions.CultureInvariant))
            { throw Invalid("The export scheme scope has not been verified."); }
            if (rule?["verifiedXmlSchemes"]?.Values<string>().Contains(parameters["CONFIGSCHEME"], StringComparer.Ordinal) != true)
            { throw Invalid("CONFIGSCHEME is not a verified XML scheme."); }
            using var scheme = new SchemeSetting();
            scheme.Init(node);
            if (!scheme.CheckIfSchemeExists(parameters["CONFIGSCHEME"])) { throw Invalid("CONFIGSCHEME must name an existing scheme in the verified scope."); }
            data.Before["scheme:lastUsed"] = scheme.GetLastUsed() ?? string.Empty;
        }
    }

    private static BridgeErrorException Invalid(string message) => new(ErrorReason.InvalidArgument, message);
}
