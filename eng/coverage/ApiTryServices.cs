using System.Text.Json;
using System.Text.RegularExpressions;
using EplanMcp.Host.Tools;

namespace EplanMcp.Coverage;

/// <summary>Offline service construction and side-effect restrictions, independent of runtime reflection.</summary>
internal static class ApiTryServices
{
    internal sealed record Service(bool Disposable, string? Reason);
    private static readonly string[] _namespaces = ["Eplan.EplApi.HEServices", "Eplan.EplApi.EServices", "Eplan.EplApi.EServices.Ged", "Eplan.EplApi.DataModel.Planning", "Eplan.EplApi.DataModel.Topology"];
    private static string Text(JsonElement value, string name) => value.TryGetProperty(name, out JsonElement field) ? field.GetString() ?? "" : "";
    private static IEnumerable<JsonElement> Lines(string path) => File.Exists(path) ? File.ReadLines(path).Select(l => JsonSerializer.Deserialize<JsonElement>(l)) : [];
    internal static Dictionary<string, Service> Load(string root) => Build(
        [.. Lines(Path.Combine(root, "catalog/eplan-2026.0.3/types.jsonl"))],
        [.. _namespaces.SelectMany(ns => Lines(Path.Combine(root, "catalog/eplan-2026.0.3/members/" + ns + ".jsonl")))]);
    internal static Dictionary<string, Service> Build(JsonElement[] types, JsonElement[] members)
    {
        var byType = types.ToDictionary(t => Text(t, "id")[2..], StringComparer.Ordinal);
        bool Derives(string type, string parent) => type == parent || byType.TryGetValue(type, out JsonElement t) &&
            (Derives(Text(t, "base"), parent) || t.TryGetProperty("interfaces", out JsonElement interfaces) && interfaces.EnumerateArray().Any(i => Derives(i.GetString()!, parent)));
        bool Public(JsonElement t) => !t.TryGetProperty("access", out _) && !t.TryGetProperty("excluded", out _)
            && (!t.TryGetProperty("outer", out JsonElement outer) || byType.TryGetValue(outer.GetString()![2..], out JsonElement owner) && Public(owner));
        return types.Where(t => Text(t, "kind") == "class" && _namespaces.Contains(Text(t, "ns"), StringComparer.Ordinal)
                && !Derives(Text(t, "id")[2..], "Eplan.EplApi.DataModel.StorableObject"))
            .ToDictionary(t => Text(t, "id")[2..], t => new Service(Derives(Text(t, "id")[2..], "System.IDisposable"),
                !Public(t) ? "service_type_excluded" : Text(t, "modifiers").Contains("abstract", StringComparison.Ordinal) ? "abstract_service"
                : members.Any(m => Text(m, "type") == Text(t, "id") && Text(m, "kind") == "constructor" && Public(m)
                    && (!m.TryGetProperty("static", out JsonElement s) || !s.GetBoolean()) && (!m.TryGetProperty("params", out JsonElement p) || p.GetArrayLength() == 0)) ? null : "no_public_default_constructor"), StringComparer.Ordinal);
    }
    private static bool Match(string value, string pattern) => Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    internal static bool SideEffectParameter(ApiParameter p) => Match(p.Name, "dialog|dlg|show|select|lock|write|update|create|remove|delete|save|store|generate|rebuild|overwrite|change|set");
    internal static string? Exclusion(ApiEntry entry, ApiDetail member)
    {
        string type = entry.Type[2..], operation = type + "." + entry.Name;
        if (Match(operation, "licen[cs]|\\.(Exit|Quit|Shutdown)(Application)?$"))
        { return "licensing_or_application_exit"; }
        if (Match(type, "\\.(Masterdata|PartsService)(\\.|$)") || Match(entry.Name, "MasterData|PartsDatabase|PartToSystem"))
        { return "masterdata_or_parts_management"; }
        if (Match(type, "\\.(Settings|.*SchemeSetting)(\\.|$)") || Match(entry.Name, "Settings|Scheme|Schema"))
        { return "settings_or_scheme_parameter"; }
        if (entry.Name.EndsWith("TXN", StringComparison.Ordinal))
        { return "owns_transaction"; }
        foreach (ApiParameter p in member.Parameters!)
        {
            string descriptor = p.Name + " " + p.Type;
            if (Match(descriptor, "stream"))
            { return "stream_parameter"; }
            if (Match(descriptor, "database|\\bstrServer\\b|\\.Sql"))
            { return "database_parameter"; }
            if (Match(descriptor, "masterdata|partsmanagement"))
            { return "masterdata_or_parts_management"; }
            if (Match(descriptor, "settings?|schem[ae]"))
            { return "settings_or_scheme_parameter"; }
            if (Match(descriptor, "printer"))
            { return "printer_parameter"; }
            if (Match(descriptor, "url|uri"))
            { return "url_parameter"; }
            if (Match(descriptor, "file|directory|dir(?:name|$| )|folder|path|archive|macro|xml|template") || p.Type == "System.String" && Match(p.Name, "project")
                || type == "Eplan.EplApi.HEServices.PlcService" && entry.Name == "ImportData" && p.Name == "strFullName")
            { return "external_path_parameter"; }
        }
        // These operations can use configured destinations even without an explicit path argument.
        if (Match(type, "\\.(Print|Backup|Restore|Export3?D?|Import3?D?)(\\.|$)") || Match(entry.Name, "CreateComparisonProject|CreateRevision|^Merge$|PackProject|UnpackProject|CopyProject"))
        { return "external_destination"; }
        if (Match(type, "\\.Ged\\.(Interaction|InsertInteraction|SelectionInteraction)$") || Match(operation, "ConnectionService3D.ShowFillingDegree"))
        { return "shared_interactive_state"; }
        return null;
    }
}
