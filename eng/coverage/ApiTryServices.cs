using System.Text.Json;
using System.Text.RegularExpressions;
using EplanMcp.Host.Tools;

namespace EplanMcp.Coverage;

/// <summary>Offline service construction and side-effect restrictions, independent of runtime reflection.</summary>
internal static class ApiTryServices
{
    /// <summary><paramref name="ProjectConstructor"/>: the service is built from the approved Project target
    /// instead of a parameterless constructor (owner 2026-11-14, same batch as ADR-0056).</summary>
    internal sealed record Service(bool Disposable, string? Reason, bool ProjectConstructor = false, string? Concrete = null);
    private static readonly string[] _namespaces = ["Eplan.EplApi.HEServices", "Eplan.EplApi.EServices", "Eplan.EplApi.EServices.Ged", "Eplan.EplApi.DataModel.Planning", "Eplan.EplApi.DataModel.Topology"];
    private static string Text(JsonElement value, string name) => value.TryGetProperty(name, out JsonElement field) ? field.GetString() ?? "" : "";
    private static IEnumerable<JsonElement> Lines(string path) => File.Exists(path) ? File.ReadLines(path).Select(l => JsonSerializer.Deserialize<JsonElement>(l)) : [];
    /// <summary>ADR-0054: the one parts master data root; a fresh instance per call opens only the database EPLAN is configured with.</summary>
    internal const string PartsManagement = "Eplan.EplApi.MasterData.MDPartsManagement";
    internal const string PathInfo = "Eplan.EplApi.DataModel.PathInfo";
    /// <summary>
    /// ADR-0056: the types allowed as trial roots by name, with whether they are IDisposable; adding one means
    /// amending that ADR. They touch no project, file, setting, database, printer or licence: UnitParser and ISOCode
    /// parse units and language codes. PathMap, Languages and DocumentTypeManager were considered on 2026-11-14 and
    /// left out: every member of theirs is static, so they were already callable and the gap list was simply
    /// reporting their declaring type's reachability, which a static member does not need.
    /// </summary>
    internal static readonly (string Type, bool Disposable)[] Utilities =
    [
        ("Eplan.EplApi.Base.UnitParser", true),
        ("Eplan.EplApi.Base.ISOCode", true),
    ];
    private static readonly string[] _partsManagementReads = ["OpenDatabase", "PartsDatabaseState"];
    internal static bool MasterDataHandle(string type) => type.StartsWith("Eplan.EplApi.MasterData.", StringComparison.Ordinal);
    internal static Dictionary<string, Service> Load(string root)
    {
        Dictionary<string, Service> services = Build(
            [.. Lines(Path.Combine(root, "catalog/eplan-2026.0.3/types.jsonl"))],
            [.. _namespaces.SelectMany(ns => Lines(Path.Combine(root, "catalog/eplan-2026.0.3/members/" + ns + ".jsonl")))]);
        services[PartsManagement] = new(false, null);
        // Read-only path settings (Articles, Macros, Symbols ...); ProjectManager.Paths would expose project open and remove.
        services[PathInfo] = new(true, null);
        // ADR-0056: utility types by name, never by rule. These parse units and language codes and touch no project,
        // file, setting, database, printer or licence; both are IDisposable and are disposed per call like a service.
        foreach ((string utility, bool disposable) in Utilities)
        { services[utility] = new(disposable, null); }
        return services;
    }
    /// <summary>
    /// A service whose only public constructor takes the project is built from the Project the call already targets:
    /// the receiver shape of every service is that same approved Project locator, so this adds no new reach beyond
    /// the project in hand (owner 2026-11-14: project messages and sub-project collections were unreachable only
    /// because the generator insisted on a parameterless constructor).
    /// </summary>
    private static bool ProjectConstructor(JsonElement[] members, string typeId) => members.Any(m =>
        Text(m, "kind") == "constructor" && Text(m, "type") == typeId && !m.TryGetProperty("access", out _)
        && m.TryGetProperty("params", out JsonElement p) && p.GetArrayLength() == 1
        && p[0].GetProperty("type").GetString() == "Eplan.EplApi.DataModel.Project");

    internal static Dictionary<string, Service> Build(JsonElement[] types, JsonElement[] members)
    {
        var byType = types.ToDictionary(t => Text(t, "id")[2..], StringComparer.Ordinal);
        bool Derives(string type, string parent) => type == parent || byType.TryGetValue(type, out JsonElement t) &&
            (Derives(Text(t, "base"), parent) || t.TryGetProperty("interfaces", out JsonElement interfaces) && interfaces.EnumerateArray().Any(i => Derives(i.GetString()!, parent)));
        bool Public(JsonElement t) => !t.TryGetProperty("access", out _) && !t.TryGetProperty("excluded", out _)
            && (!t.TryGetProperty("outer", out JsonElement outer) || byType.TryGetValue(outer.GetString()![2..], out JsonElement owner) && Public(owner));
        bool Constructible(JsonElement t) => members.Any(m => Text(m, "kind") == "constructor" && Text(m, "type") == Text(t, "id")
                && !m.TryGetProperty("access", out _) && (!m.TryGetProperty("params", out JsonElement q) || q.GetArrayLength() == 0))
            || ProjectConstructor(members, Text(t, "id"));
        // A member declared on an abstract service is called on an instance, and the catalogue counts it under the
        // base type. Where exactly one public concrete subclass is constructible, that subclass is the instance to
        // build; several would make the choice ambiguous, so those stay abstract_service (owner 2026-11-14).
        string? Concrete(JsonElement t)
        {
            if (!Text(t, "modifiers").Contains("abstract", StringComparison.Ordinal))
            { return null; }
            string[] children = [.. types.Where(c => Text(c, "kind") == "class" && Text(c, "base") == Text(t, "id")[2..]
                && Public(c) && !Text(c, "modifiers").Contains("abstract", StringComparison.Ordinal) && Constructible(c))
                .Select(c => Text(c, "id")[2..])];
            return children.Length == 1 ? children[0] : null;
        }
        return types.Where(t => Text(t, "kind") == "class" && _namespaces.Contains(Text(t, "ns"), StringComparer.Ordinal)
                && !Derives(Text(t, "id")[2..], "Eplan.EplApi.DataModel.StorableObject"))
            .ToDictionary(t => Text(t, "id")[2..], t => new Service(Derives(Text(t, "id")[2..], "System.IDisposable"),
                !Public(t) ? "service_type_excluded" : Text(t, "modifiers").Contains("abstract", StringComparison.Ordinal) && Concrete(t) is null ? "abstract_service"
                : members.Any(m => Text(m, "type") == Text(t, "id") && Text(m, "kind") == "constructor" && Public(m)
                    && (!m.TryGetProperty("static", out JsonElement s) || !s.GetBoolean()) && (!m.TryGetProperty("params", out JsonElement p) || p.GetArrayLength() == 0)) ? null
                : ProjectConstructor(members, Text(t, "id")) || Concrete(t) is not null ? null : "no_public_default_constructor",
                ProjectConstructor(members, Concrete(t) ?? Text(t, "id")), Concrete(t)), StringComparer.Ordinal);
    }
    private static bool Match(string value, string pattern) => Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    internal static bool SideEffectParameter(ApiParameter p) => Match(p.Name, "dialog|dlg|show|select|lock|write|update|create|remove|delete|save|store|generate|rebuild|overwrite|change|set");
    internal static string? Exclusion(ApiEntry entry, ApiDetail member)
    {
        string type = entry.Type[2..], operation = type + "." + entry.Name;
        if (Match(operation, "licen[cs]|\\.(Exit|Quit|Shutdown)(Application)?$"))
        { return "licensing_or_application_exit"; }
        // ADR-0054: never create, select, delete, unlock or import databases, or drive the parts management dialog.
        if (type == PartsManagement && !(_partsManagementReads.Contains(entry.Name, StringComparer.Ordinal) && member.Parameters!.Length == 0))
        { return "masterdata_management"; }
        bool masterData = MasterDataHandle(type);
        if (!masterData && (Match(type, "\\.(Masterdata|PartsService)(\\.|$)") || Match(entry.Name, "MasterData|PartsDatabase|PartToSystem")))
        { return "masterdata_or_parts_management"; }
        if (Match(type, "\\.(Settings|.*SchemeSetting)(\\.|$)") || Match(entry.Name, "Settings|Scheme|Schema"))
        { return "settings_or_scheme_parameter"; }
        if (entry.Name.EndsWith("TXN", StringComparison.Ordinal))
        { return "owns_transaction"; }
        foreach (ApiParameter p in member.Parameters!)
        {
            // Master data handle parameters are session handles of the same configured database, not database selectors.
            string descriptor = p.Name + " " + (masterData && MasterDataHandle(p.Type) ? "" : p.Type);
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
            if (Match(descriptor, "file|directory|dir(?:name|$| )|folder|path|archive|macro|xml|template") || (p.Type == "System.String" || ApiTryGenerator.Element(p.Type.TrimEnd('@')) == "System.String") && Match(p.Name, "project")
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
