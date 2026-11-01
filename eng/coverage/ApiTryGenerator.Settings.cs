using System.Text.RegularExpressions;
using EplanMcp.Host.Tools;
namespace EplanMcp.Coverage;

internal static partial class ApiTryGenerator
{
    private static bool SettingsType(ApiEntry e) => e.Type is "T:Eplan.EplApi.Base.Settings" or "T:Eplan.EplApi.Base.SettingNode" or "T:Eplan.EplApi.Base.SchemeSetting"
        or "T:Eplan.EplApi.DataModel.ProjectSettings" or "T:Eplan.EplApi.DataModel.ProjectSettingNode" or "T:Eplan.EplApi.DataModel.ProjectSchemeSetting" or "T:Eplan.EplApi.DataModel.Project.DeviceTagSettings"
        || e.Row.Group == "O:Eplan.EplApi.DataModel.Project.Settings";
    internal static bool SettingsWrite(ApiEntry e) => e.Row.Kind == "method" && SettingsType(e) && Regex.IsMatch(e.Name, "^(Set|Save|Load|Import|Export|Reset|Add|Delete|Remove|Clear|Merge|ForceReload|ReadSettings|Write|Create|Copy)", RegexOptions.CultureInvariant);
    private static bool SettingsList(ApiEntry e) => e.Name is "GetListOfNodes" or "GetListOfSettings" or "GetListOfAllSettings";
    private static bool SettingsProperty(ApiEntry e) => e.Row.Kind == "property";
    private static bool SettingsRootProperty(ApiEntry e) => SettingsProperty(e) && e.Type != "T:Eplan.EplApi.Base.SchemeSetting";
    internal static string? SettingsExclusion(ApiEntry e, ApiDetail d)
    {
        if (SettingsWrite(e))
        { return "settings_write"; }
        if (e.Row.ExcludedReason is not null || d.ExcludedReason is not null)
        { return "catalog:" + (e.Row.ExcludedReason ?? d.ExcludedReason); }
        if (d.Static || d.Access is not null)
        { return "static_or_nonpublic"; }
        if (e.Name is "Init" or "Dispose" || e.Name.StartsWith('#'))
        { return "lifecycle_or_creation"; }
        bool read = SettingsProperty(e) && d.Accessors?.Split(';').Contains("get", StringComparer.Ordinal) == true
            || SettingsList(e) && d.Parameters is [{ Type: "System.Collections.Specialized.StringCollection@" }, { Type: "System.Boolean" }]
            || Regex.IsMatch(e.Name, "^(ExistSetting|Get(Bool|Double|Numeric|String|ExpandedString|MultiLangString)(Setting|Default)|GetCountOf(DefaultValues|Values|Nodes|Settings)|GetDescription|GetTypeOfSetting|IsMainNodeReadOnly|GetNodePath(Dot)?|GetCount|GetLastUsed|GetName|GetLocalizedNameSettingPath|CheckIfSchemeExists|CountSetting)$", RegexOptions.CultureInvariant);
        return read && (SettingsList(e) || Scalar(d.ReturnType, null) || d.ReturnType is "System.UInt32" or "Eplan.EplApi.Base.ISettings.SettingType" or "Eplan.EplApi.Base.MultiLangString" or "Eplan.EplApi.DataModel.ProjectSettings")
            && (SettingsList(e) || d.Parameters!.All(p => p.Type is "System.String" or "System.Int32")) ? null : "unsupported_settings_read";
    }
    private static IEnumerable<(ApiEntry Entry, ApiDetail Member)> SettingsMembers(ApiIndexData data) => data.Entries.Where(SettingsType)
        .SelectMany(e => e.Details.Where(d => d.Kind == "overload" && SettingsExclusion(e, d) is null).Select(d => (e, d)));
    private static bool SettingsBlocked(ApiEntry e) => SettingsType(e) && !e.Details.Any(d => d.Kind == "overload" && SettingsExclusion(e, d) is null);
    private static ApiParameter[] SettingsParameters(ApiEntry e, ApiDetail d) => SettingsRootProperty(e) ? [] : [new("node", "System.String"),
        .. SettingsList(e) ? new ApiParameter[] { new("offset", "System.Int32"), new("limit", "System.Int32") } : d.Parameters!];
    private static string SettingsReport(ApiIndexData data, string original)
    {
        string additions = string.Concat(data.Entries.Where(SettingsType).SelectMany(e => e.Details.Where(d => d.Kind == "overload").Select(d =>
        e.Row.Group + "\t" + d.Id + "\t" + (SettingsExclusion(e, d) is { } reason ? "excluded\t" + reason : "read\tsettings_read_only") + "\n"
        + (d.Accessors?.Split(';').Contains("set", StringComparer.Ordinal) == true ? e.Row.Group + "\t" + d.Id + ":set\texcluded\tsettings_write\n" : ""))));
        if (additions.Length == 0)
        { return original; }
        var ids = additions.TrimEnd('\n').Split('\n').Select(l => l.Split('\t')[1]).ToHashSet(StringComparer.Ordinal);
        return string.Join("\n", original.TrimEnd('\n').Split('\n').Where(l => !ids.Contains(l.Split('\t')[1]))
            .Concat(additions.TrimEnd('\n').Split('\n')).OrderBy(l => l == "group\tsignature\tstatus\treason" ? "" : l, StringComparer.Ordinal)) + "\n";
    }
    private static string SettingsCatalog(ApiIndexData data)
    {
        var members = SettingsMembers(data).ToArray();
        return "    public static bool Settings(string group) => group switch { " + string.Join(" ", members.Select(s => Literal(s.Entry.Row.Group) + " => true,")) + " _ => false };\n"
            + "    public static string[][] SettingsMethods(string group) => group switch\n    {\n"
            + string.Join("\n", members.GroupBy(s => s.Entry.Row.Group).Select(g => "        " + Literal(g.Key) + " => [" + string.Join(", ", g.Select(s => "[" + string.Join(", ", new[] { s.Entry.Type[2..], s.Member.Signature! }.Concat(SettingsParameters(s.Entry, s.Member).Select(p => p.Name)).Select(Literal)) + "]")) + "],"))
            + "\n        _ => [],\n    };\n    private static string[]? SettingsReceiverShape(string signature) => signature switch { " + string.Join(" ", members.Select(s => Literal(s.Member.Signature!) + " => [\"Eplan.EplApi.DataModel.Project\"],")) + " _ => null };\n"
            + "    private static string[]? SettingsShape(string group, System.Collections.Generic.IEnumerable<string> names) => (group, string.Join(\"|\", System.Linq.Enumerable.OrderBy(System.Linq.Enumerable.Where(names, n => n != \"target\"), n => n, System.StringComparer.Ordinal))) switch\n    {\n"
            + string.Join("\n", members.Select(s => "        (" + Literal(s.Entry.Row.Group) + ", " + Literal(string.Join("|", SettingsParameters(s.Entry, s.Member).Select(p => p.Name).Order(StringComparer.Ordinal))) + ") => ["
                + string.Join(", ", new[] { s.Entry.Type[2..], s.Member.Signature! }.Concat(SettingsParameters(s.Entry, s.Member).Select(p => p.Name)).Select(Literal)) + "],"))
            + "\n        _ => null,\n    };\n    private static string[]? SettingsParameterShape(string signature, string name) => (signature, name) switch\n    {\n"
            + string.Join("\n", members.SelectMany(s => SettingsParameters(s.Entry, s.Member).Select(p => "        (" + Literal(s.Member.Signature!) + ", " + Literal(p.Name) + ") => [" + Literal(p.Type) + "],")))
            + "\n        _ => null,\n    };\n";
    }
    private static string SettingsDispatch(ApiIndexData data)
    {
        var members = SettingsMembers(data).ToArray();
        return "    private static bool SettingsSignature(string signature) => signature switch { " + string.Join(" ", members.Select(s => Literal(s.Member.Signature!) + " => true,")) + " _ => false };\n"
            + "    private static object? InvokeSettings(string signature, Project project, JObject parameters)\n    {\n        switch (signature)\n        {\n"
            + string.Join("\n", members.Select(s =>
            {
                string type = s.Entry.Type[2..], name = s.Entry.Name;
                string receiver = type.EndsWith(".Settings", StringComparison.Ordinal) ? "new global::Eplan.EplApi.Base.Settings()"
                    : type.EndsWith(".ProjectSettings", StringComparison.Ordinal) ? "project.Settings"
                    : type.EndsWith(".SchemeSetting", StringComparison.Ordinal) ? "EplanApiTryDispatcher.OpenSettingsScheme(project, nodePath)"
                    : "EplanApiTryDispatcher.OpenSettingsNode(project, nodePath)";
                string body;
                if (SettingsRootProperty(s.Entry))
                { body = "return " + (name == "Settings" ? "\"PROJECT\"" : type.EndsWith(".DeviceTagSettings", StringComparison.Ordinal) ? "new global::" + type + "(project)." + name : "throw new System.InvalidOperationException(\"Unsupported settings property.\")") + ";"; }
                else
                {
                    string args = string.Join(", ", s.Member.Parameters!.Select(p => p.Type == "System.String" ? p.Name == "strSettingPath" && type is "Eplan.EplApi.Base.Settings" or "Eplan.EplApi.DataModel.ProjectSettings"
                        ? "EplanApiTryDispatcher.SettingsFullPath(nodePath, parameters[\"strSettingPath\"]!.Value<string>()!)" : "parameters[" + Literal(p.Name) + "]!.Value<string>()" : "parameters[" + Literal(p.Name) + "]!.Value<int>()"));
                    body = "string nodePath = parameters[\"node\"]!.Value<string>()!; using var receiver = " + receiver + "; "
                        + (SettingsList(s.Entry) ? "var list = new System.Collections.Specialized.StringCollection(); receiver." + name + "(ref list, false); return EplanApiTryDispatcher.SettingsPage(list, parameters);"
                        : s.Member.ReturnType == "Eplan.EplApi.Base.MultiLangString" ? "using var text = receiver." + name + (SettingsProperty(s.Entry) ? "" : "(" + args + ")") + "; return text.ToString();" : "return receiver." + name + (SettingsProperty(s.Entry) ? "" : "(" + args + ")") + ";");
                }
                return "            case " + Literal(s.Member.Signature!) + ": { " + body + " }";
            })) + "\n            default: throw new System.InvalidOperationException(\"No compiled settings read.\");\n        }\n    }\n";
    }
}
