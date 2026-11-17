using System.Collections.Specialized;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanApiTryDispatcher
{
    // A67: constructors and Init bind enumerated handles; no public Set*/Init trial is generated.
    internal static SettingNode OpenSettingsNode(Project project, string path) => path == "PROJECT" || path.StartsWith("PROJECT.", StringComparison.Ordinal)
        ? new ProjectSettingNode(project, path.Length == 7 ? "" : path.Substring(8)) : new SettingNode(path);
    internal static string SettingsFullPath(string node, string leaf) => (node == "PROJECT" ? "" : node.StartsWith("PROJECT.", StringComparison.Ordinal) ? node.Substring(8) + "." : node + ".") + leaf;
    /// <summary>
    /// ADR-0036/0059 step 3b: sets one enumerated scalar setting and reads it back. A PROJECT setting belongs to the
    /// scratch project and stays; a USER or STATION setting would change every project, so its previous value is
    /// written back at once and must read back unchanged, otherwise the outcome is unknown and writes are suspended.
    /// </summary>
    internal static string WriteSetting(ISettings settings, string node, string path, int index, string kind, JToken value)
    {
        string before = ReadSetting(settings, path, index, kind);
        Exception? failure = null;
        string after;
        try
        {
            SetSetting(settings, path, index, kind, value);
            after = ReadSetting(settings, path, index, kind);
        }
        catch (Exception ex) { failure = ex; after = before; }
        bool global = !(node == "PROJECT" || node.StartsWith("PROJECT.", StringComparison.Ordinal));
        if (global)
        {
            SetSetting(settings, path, index, kind, JToken.Parse(before));
            if (ReadSetting(settings, path, index, kind) != before)
            {
                throw new BridgeErrorException(ErrorReason.OutcomeUnknown, "The " + node.Split('.')[0] + " setting " + path + " could not be restored to its previous value; ask the engineer to check it in EPLAN.", failure ?? new InvalidOperationException("Restore read back a different value."))
                { Error = { Execution = ExecutionState.Unknown } };
            }
        }
        if (failure is not null)
        { throw failure; }
        return JsonConvert.SerializeObject(new { before = JToken.Parse(before), readBack = JToken.Parse(after), restored = global });
    }
    private static string ReadSetting(ISettings settings, string path, int index, string kind) => JsonConvert.SerializeObject(kind switch
    {
        "Bool" => (object)settings.GetBoolSetting(path, index),
        "Double" => settings.GetDoubleSetting(path, index),
        "Numeric" => settings.GetNumericSetting(path, index),
        _ => settings.GetStringSetting(path, index),
    });
    private static void SetSetting(ISettings settings, string path, int index, string kind, JToken value)
    {
        switch (kind)
        {
            case "Bool":
                settings.SetBoolSetting(path, value.Value<bool>(), index);
                break;
            case "Double":
                settings.SetDoubleSetting(path, value.Value<double>(), index);
                break;
            case "Numeric":
                settings.SetNumericSetting(path, value.Value<int>(), index);
                break;
            default:
                settings.SetStringSetting(path, value.Value<string>(), index);
                break;
        }
    }
    internal static SchemeSetting OpenSettingsScheme(Project project, string path)
    {
        SchemeSetting scheme = path == "PROJECT" || path.StartsWith("PROJECT.", StringComparison.Ordinal) ? new ProjectSchemeSetting() : new SchemeSetting();
        try
        {
            if (scheme is ProjectSchemeSetting scoped)
            { scoped.Init(path.Length == 7 ? "" : path.Substring(8), project); }
            else
            { scheme.Init(path); }
            return scheme;
        }
        catch { scheme.Dispose(); throw; }
    }
    internal static string SettingsPage(StringCollection list, JObject parameters)
    {
        string[] items = list.Cast<string>().OrderBy(p => p, StringComparer.Ordinal).ToArray();
        int offset = parameters["offset"]!.Value<int>(), limit = parameters["limit"]!.Value<int>();
        return JsonConvert.SerializeObject(new { items = items.Skip(offset).Take(limit).ToArray(), total = items.Length, nextOffset = offset + Math.Min(limit, Math.Max(0, items.Length - offset)) < items.Length ? (int?)(offset + limit) : null });
    }
    private static void CheckSettings(ApiTryArgs args)
    {
        using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
        JObject parameters = ApiTryPlanOperation.Parameters(args.Parameters);
        if (parameters["node"] is null)
        { return; }
        Project project = EplanProjects.ByPath(args.Project);
        string path = parameters["node"]!.Value<string>()!;
        if (!ApiTrySettingsValidation.Enumerated(path, parent =>
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            using SettingNode current = OpenSettingsNode(project, parent);
            var names = new StringCollection();
            current.GetListOfNodes(ref names, false);
            return names.Cast<string>().ToArray();
        }))
        { throw new BridgeErrorException(ErrorReason.PlanStale, "Settings node changed or was not enumerated; list children again from USER, STATION or PROJECT."); }
        string? leaf = parameters["strSettingPath"]?.Value<string>() ?? parameters["strName"]?.Value<string>();
        if (leaf is null)
        { return; }
        using SchemeSetting? scheme = parameters["strName"] is null && args.Group.IndexOf(".SchemeSetting.", StringComparison.Ordinal) >= 0 ? OpenSettingsScheme(project, path) : null;
        using SettingNode node = scheme is null ? OpenSettingsNode(project, path) : scheme.GetNodeHandle();
        var leaves = new StringCollection();
        if (parameters["strName"] is not null)
        { node.GetListOfNodes(ref leaves, false); }
        else
        { node.GetListOfSettings(ref leaves, false); }
        if (!leaves.Cast<string>().Contains(leaf, StringComparer.Ordinal))
        { throw new BridgeErrorException(ErrorReason.PlanStale, "Settings path was not enumerated; list settings or schemes again before reading."); }
    }
}
