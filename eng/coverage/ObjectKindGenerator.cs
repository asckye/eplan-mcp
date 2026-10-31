using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
namespace EplanMcp.Coverage;
/// <summary>Closed discovery/property dispatch from catalog inheritance and public, non-excluded members.</summary>
internal static class ObjectKindGenerator
{
    internal const string TypesPath = "src/EplanMcp.Contracts/Generated/ObjectReadTypes.g.cs";
    internal const string DispatchPath = "src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs";
    internal const string TablePath = "docs/tools/eplan_object_kinds.g.md";
    private const string Prefix = "Eplan.EplApi.DataModel.";
    internal sealed record Kind(string Name, string Type, string Dictionary, string PropertyOwner, string[] Sources, string? Reason);
    private static string Q(string value) => JsonSerializer.Serialize(value);
    private static string Text(JsonElement value, string key) => value.TryGetProperty(key, out JsonElement field) ? field.GetString() ?? "" : "";
    internal static Kind[] Build(JsonElement[] types, ApiIndexData data)
    {
        var byType = types.ToDictionary(t => Text(t, "id")[2..], StringComparer.Ordinal);
        IEnumerable<string> Ancestors(string type)
        {
            while (byType.TryGetValue(type, out JsonElement t))
            { yield return type; type = Text(t, "base"); }
        }
        bool Derives(string type, string parent) => Ancestors(type).Contains(parent, StringComparer.Ordinal);
        bool Safe(ApiEntry e, ApiDetail d) => e.Row.ExcludedReason is null && d.ExcludedReason is null && d.Access is null && !d.Static;
        bool Has(string id) => data.Entries.Any(e => e.Details.Any(d => d.Id == id && Safe(e, d)));
        var properties = data.Entries.Where(e => e.Name == "Properties").ToDictionary(e => e.Type[2..], StringComparer.Ordinal);
        var dictionaries = PropertyCatalog.Entries.Where(e => !e.Obsolete).Select(e => e.ObjectType).ToHashSet(StringComparer.Ordinal);
        var result = new List<Kind>();
        foreach (JsonElement t in types.Where(t => Derives(Text(t, "id")[2..], Prefix + "StorableObject")).OrderBy(t => Text(t, "id"), StringComparer.Ordinal))
        {
            string type = Text(t, "id")[2..], name = type[(type.LastIndexOf('.') + 1)..];
            string owner = Ancestors(type).FirstOrDefault(properties.ContainsKey) ?? "";
            ApiEntry? property = properties.GetValueOrDefault(owner);
            string list = property?.Details.FirstOrDefault(d => d.Kind == "overload")?.ReturnType ?? "";
            string dictionary = Ancestors(list).Select(n => n[(n.LastIndexOf('.') + 1)..].Replace("PropertyListComplete", "", StringComparison.Ordinal).Replace("PropertyList", "", StringComparison.Ordinal)).FirstOrDefault(dictionaries.Contains) ?? "StorableObject";
            string? reason = t.TryGetProperty("excluded", out _) ? "catalog_excluded" : Text(t, "modifiers").Contains("abstract", StringComparison.Ordinal) ? "abstract_type"
                : Derives(type, Prefix + "ArticleReference") ? "transient_reference:A60" : Derives(type, Prefix + "Graphics.PropertyPlacement") ? "shared_parent_identity:A57"
                : type == Prefix + "E3D.MateEntry" ? "unstable_identity:A62" : null;
            var sources = new List<string>();
            foreach (ApiEntry e in data.Entries.Where(e => e.Type == "T:" + Prefix + "DMObjectsFinder" && e.Name.StartsWith("Get", StringComparison.Ordinal) && !e.Name.Contains("WithCF", StringComparison.Ordinal)))
            {
                if (e.Name == "GetStorableObjects" && (type is Prefix + "Page" or Prefix + "MasterData.Symbol" || Derives(type, Prefix + "Graphics.PropertyPlacement"))
                    || e.Name == "GetFunctions3D" && Derives(type, Prefix + "E3D.InstallationSpace") || e.Name == "GetPlacements3D" && Derives(type, Prefix + "E3D.MateEntry"))
                { continue; }
                if (e.Details.Any(d => Safe(e, d) && !d.Id.Contains('`', StringComparison.Ordinal) && d.Parameters is { Length: 1 } && (Derives(d.Parameters[0].Type, Prefix + "AbstractDMObjectFilter") || e.Name.EndsWith("WithFilterScheme", StringComparison.Ordinal) && d.Parameters[0].Type == "System.String") && d.ReturnType?.EndsWith("[]", StringComparison.Ordinal) == true && Derives(type, d.ReturnType[..^2])))
                { sources.Add("finder." + e.Name + "(null)"); }
            }
            if (Derives(type, Prefix + "Placement") && Has("P:" + Prefix + "Page.AllPlacements") && Has("M:" + Prefix + "DMObjectsFinder.GetPages(" + Prefix + "PagesFilter)"))
            { sources.Add("Placements(finder, check, pageName)"); }
            if (Derives(type, Prefix + "E3D.InstallationSpace") && Has("P:" + Prefix + "Project.InstallationSpaces"))
            { sources.Add("project.InstallationSpaces"); }
            if (type == Prefix + "E3D.Connection3D" && Has("P:" + Prefix + "Project.InstallationSpaces") && Has("P:" + Prefix + "E3D.InstallationSpace.Connection3Ds"))
            { sources.Add("project.InstallationSpaces.SelectMany(s => s.Connection3Ds)"); }
            if (type == Prefix + "Project")
            { sources.Add("new[] { project }"); }
            // GetStorableObjects explicitly omits property placements and symbols; A57/A60 override generic paths.
            reason ??= property is null || !property.Details.Any(d => Safe(property, d) && d.Accessors?.Split(';').Contains("get", StringComparer.Ordinal) == true) ? "no_safe_properties" : sources.Count == 0 ? "no_safe_enumeration" : null;
            result.Add(new(name, type, dictionary, owner, [.. sources.Distinct(StringComparer.Ordinal)], reason));
        }
        foreach ((string alias, string type) in new[] { ("BlackBox", "BoxedDevice"), ("PLCTerminal", "EObjects.Terminal") })
        {
            Kind? original = result.Find(k => k.Type == Prefix + type);
            result.Add(original is null ? new(alias, Prefix + type, "Function", "", [], "no_catalog_type") : original with { Name = alias, Sources = [] });
        }
        foreach (string name in dictionaries.Except(result.Select(k => k.Name), StringComparer.Ordinal))
        { result.Add(new(name, "", name, "", [], "not_storable_catalog_type")); }
        return [.. result.OrderBy(k => k.Name, StringComparer.Ordinal)];
    }
    internal static Kind[] Load(string root) => Build(File.Exists(Path.Combine(root, "catalog/eplan-2026.0.3/types.jsonl"))
        ? [.. File.ReadLines(Path.Combine(root, "catalog/eplan-2026.0.3/types.jsonl")).Select(l => JsonSerializer.Deserialize<JsonElement>(l))] : [], ApiIndexGenerator.Data(root));
    internal static Dictionary<string, string[]> Kinds(string root)
    {
        Kind[] kinds = Load(root);
        var types = File.Exists(Path.Combine(root, "catalog/eplan-2026.0.3/types.jsonl")) ? File.ReadLines(Path.Combine(root, "catalog/eplan-2026.0.3/types.jsonl")).Select(l => JsonSerializer.Deserialize<JsonElement>(l)).ToDictionary(t => Text(t, "id")[2..], StringComparer.Ordinal) : [];
        bool Derives(string type, string parent) => type == parent || types.TryGetValue(type, out JsonElement t) && Derives(Text(t, "base"), parent);
        return types.Keys.Where(t => Derives(t, Prefix + "StorableObject") && !types[t].TryGetProperty("excluded", out _) && kinds.Any(k => k.Reason is null && Derives(k.Type, t)))
            .ToDictionary(t => t[Prefix.Length..], t => kinds.Where(k => k.Reason is null && Derives(k.Type, t)).Select(k => k.Name.ToLowerInvariant()).ToArray(), StringComparer.Ordinal);
    }
    internal static string Types(string root)
    {
        Kind[] kinds = Load(root);
        return "// <auto-generated/>\nnamespace EplanMcp.Contracts;\npublic static partial class ObjectReadTypes\n{\n"
            + "    public static System.Collections.Generic.IReadOnlyList<string> Types { get; } = System.Array.AsReadOnly(new[] { " + string.Join(", ", kinds.Select(k => Q(k.Name))) + " });\n"
            + "    public static bool Supported(string type) => type switch { " + string.Join(" ", kinds.Where(k => k.Reason is null).Select(k => Q(k.Name) + " => true,")) + " _ => false };\n"
            + "    public static string DictionaryType(string type) => type switch { " + string.Join(" ", kinds.Where(k => k.Name != k.Dictionary).Select(k => Q(k.Name) + " => " + Q(k.Dictionary) + ",")) + " _ => type };\n"
            + "    public static string UnsupportedReason(string type) => type switch { " + string.Join(" ", kinds.Where(k => k.Reason is not null).Select(k => Q(k.Name) + " => " + Q(k.Reason!) + ",")) + " _ => \"unknown_type\" };\n}\n";
    }
    internal static string Dispatch(string root)
    {
        Kind[] kinds = Load(root).Where(k => k.Reason is null && k.Sources.Length > 0).ToArray();
        return "// <auto-generated/>\n#nullable enable\nusing Eplan.EplApi.DataModel;\nusing System.Linq;\nusing System;\nusing System.Collections.Generic;\nnamespace EplanMcp.Bridge.Eplan.Adapters;\ninternal static class ObjectKindDispatch\n{\n"
            + "    internal static System.Collections.Generic.IEnumerable<StorableObject> Candidates(DMObjectsFinder finder, Project project, string kind, System.Action check, string? pageName) => kind switch\n    {\n"
            + string.Join("\n", kinds.Select(k => "        " + Q(k.Name.ToLowerInvariant()) + " => Enumerate([" + string.Join(", ", k.Sources.Select(s => "() => " + s)) + "], check),"))
            + "\n        _ => [],\n    };\n    internal static bool Matches(StorableObject obj, string kind) => kind switch\n    {\n"
            + string.Join("\n", kinds.Select(k => "        " + Q(k.Name.ToLowerInvariant()) + " => obj.GetType() == typeof(global::" + k.Type + "),"))
            + "\n        _ => false,\n    };\n    internal static UniversalPropertyList Properties(StorableObject obj) => obj.GetType().FullName switch\n    {\n"
            + string.Join("\n", kinds.Select(k => "        " + Q(k.Type) + " => ((global::" + k.PropertyOwner + ")obj).Properties,"))
            + "\n        _ => throw new EplanMcp.Bridge.Core.BridgeErrorException(EplanMcp.Contracts.ErrorReason.CapabilityMissing, \"This locator kind has no property adapter.\"),\n    };\n"
            + "    private static IEnumerable<StorableObject> Enumerate(Func<IEnumerable<StorableObject>>[] sources, Action check)\n    {\n        foreach (Func<IEnumerable<StorableObject>> source in sources)\n        {\n            check();\n            foreach (StorableObject obj in source())\n            { check(); yield return obj; }\n        }\n    }\n    private static IEnumerable<Placement> Placements(DMObjectsFinder finder, Action check, string? pageName)\n    {\n        foreach (Page page in finder.GetPages(null))\n        {\n            check();\n            if (pageName is not null && page.Name != pageName)\n            { continue; }\n            foreach (Placement placement in page.AllPlacements)\n            { check(); yield return placement; }\n        }\n    }\n\n"
            + "}\n";
    }
    internal static string Table(string root)
    {
        Kind[] kinds = Load(root);
        return "<!-- 此表由 eng/coverage generate 从目录生成，请勿手工编辑。 -->\n\n| 类型 | 状态 | 原因 / 枚举依据 | 属性字典 |\n|---|---|---|---|\n"
            + string.Join("\n", kinds.Select(k => "| " + k.Name + " | " + (k.Reason is null ? "supported" : "unsupported") + " | " + (k.Reason ?? (k.Sources.Length == 0 ? "兼容筛选名称（A60）" : string.Join(" + ", k.Sources))) + " | " + k.Dictionary + " |")) + "\n";
    }
}
