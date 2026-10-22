using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EplanMcp.Coverage.Reporting;

namespace EplanMcp.Coverage.Wrappers;

/// <summary>
/// Writes the internal building blocks catalog/wrappers.yaml selects (docs/design/2026-10-21-api-capability-coverage.md,
/// section 8), from the catalog alone: for a type, a DTO in Contracts with the selected properties, and in the Bridge a
/// class with the code that copies them (<c>Read</c>) and the overload table of each selected method group (one typed
/// method per overload, and for an overload it cannot wrap the reason); for an action row, a class with its parameters.
/// Only plain signatures are wrapped; anything else is an error or a listed skip, never a guess. The output depends on
/// the selection and the catalog only and is written with LF, so the same input gives the same bytes. Generated code is
/// never an MCP tool and never reflects at run time.
/// </summary>
internal static partial class WrapperGenerator
{
    public const string ContractsFolder = "src/EplanMcp.Contracts/Generated/";
    public const string BridgeFolder = "src/EplanMcp.Bridge.Eplan/Adapters/Generated/";

    private const string StringCollection = "System.Collections.Specialized.StringCollection";

    // The C# keyword of each System type a plain signature may use, as "System.<name>" to keyword.
    private static readonly Dictionary<string, string> _keywords =
        "String string Boolean bool Byte byte Int16 short UInt16 ushort Int32 int UInt32 uint Int64 long UInt64 ulong Single float Double double Object object"
            .Split(' ').Chunk(2).ToDictionary(pair => "System." + pair[0], pair => pair[1], StringComparer.Ordinal);

    private static readonly HashSet<string> _reserved = new(StringComparer.Ordinal)
    {
        "base", "bool", "byte", "char", "checked", "class", "const", "decimal", "default", "delegate", "double", "event", "explicit",
        "fixed", "float", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "object",
        "operator", "out", "override", "params", "private", "protected", "public", "ref", "short", "string", "struct", "this",
    };

    /// <summary>Repository-relative path to content of every file the selection gives; empty without a selection file.</summary>
    public static SortedDictionary<string, string> Files(string repositoryRoot)
    {
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (WrapperSelection.Load(repositoryRoot) is not { } selection)
        {
            return files;
        }

        string catalog = Path.Combine(repositoryRoot, "catalog", selection.Catalog);
        if (selection.Types.Count > 0)
        {
            Dictionary<string, JsonElement> types = ApiCatalog.Lines(Path.Combine(catalog, "types.jsonl")).ToDictionary(t => Text(t, "id")!, StringComparer.Ordinal);
            foreach (TypeSelection type in selection.Types)
            {
                if (!types.TryGetValue(type.Type, out JsonElement row) || !Identifier().IsMatch(SimpleName(type.Type)))
                {
                    throw Invalid($"{type.Type} is not a non-generic type of catalog/{selection.Catalog}/types.jsonl.");
                }

                JsonElement[] members = [.. ApiCatalog.Lines(Path.Combine(catalog, "members", Text(row, "ns") + ".jsonl")).Where(m => Text(m, "type") == type.Type)];
                Copied[] properties = [.. type.Properties.Select(p => Property(type, members, p, types))];
                if (properties.Length > 0)
                {
                    files[ContractsFolder + SimpleName(type.Type) + "Properties.g.cs"] = Dto(selection.Catalog, type, properties);
                }

                files[BridgeFolder + SimpleName(type.Type) + "Api.g.cs"] = Api(selection.Catalog, type, properties, [.. type.Overloads.Select(o => Overloads(type, members, o))]);
            }
        }

        if (selection.Actions.Count > 0)
        {
            using JsonDocument actions = JsonDocument.Parse(File.ReadAllText(Path.Combine(catalog, "actions.json")));
            foreach (ActionSelection action in selection.Actions)
            {
                JsonElement row = actions.RootElement.EnumerateArray().FirstOrDefault(r => Text(r, "id") == action.Action);
                files[BridgeFolder + action.Class + ".g.cs"] = row.ValueKind == JsonValueKind.Object && Identifier().IsMatch(action.Class)
                    ? Action(selection.Catalog, action, row)
                    : throw Invalid($"{action.Action} is not a row of catalog/{selection.Catalog}/actions.json, or '{action.Class}' is not a class name.");
            }
        }

        return files;
    }

    /// <summary>One copied property: its name, doc-comment ID, DTO type and initializer, and the expression that reads it.</summary>
    private sealed record Copied(string Name, string Id, string DtoType, string Initializer, string Value);

    private static Copied Property(TypeSelection type, JsonElement[] members, string name, Dictionary<string, JsonElement> types)
    {
        string id = $"P:{type.Type[2..]}.{name}";
        JsonElement row = members.FirstOrDefault(m => Text(m, "id") == id);
        if (row.ValueKind != JsonValueKind.Object || row.TryGetProperty("excluded", out _) || Text(row, "accessors")?.Split(';').Contains("get") != true)
        {
            throw Invalid($"{id} is not a readable, non-indexed property of {type.Type} that the catalog keeps (inherited ones are listed on their declaring type).");
        }

        string returns = Text(row, "returns")!;
        (string Type, string Initializer, string Value)? form = returns switch
        {
            StringCollection => ("List<string>", " = [];", $"Strings(source.{name})"),
            "System.String" => ("string?", string.Empty, $"source.{name}"),
            "System.Object" => null,
            _ when _keywords.TryGetValue(returns, out string? keyword) => (keyword, string.Empty, $"source.{name}"),
            _ when types.TryGetValue("T:" + returns, out JsonElement t) && Text(t, "kind") == "enum" => ("string", " = string.Empty;", $"source.{name}.ToString()"),
            _ => null,
        };
        return form is { } f
            ? new Copied(name, id, f.Type, f.Initializer, f.Value)
            : throw Invalid($"{id} returns {returns}, which has no plain form in a DTO; read it by hand.");
    }

    /// <summary>The overload table of one method group: a wrapper per overload with a plain signature, a reason for the others.</summary>
    private static List<string> Overloads(TypeSelection type, JsonElement[] members, string group)
    {
        string key = $"O:{type.Type[2..]}.{group}";
        JsonElement[] overloads = [.. members.Where(m => Text(m, "group") == key && Text(m, "kind") == "method").OrderBy(m => Text(m, "id"), StringComparer.Ordinal)];
        var lines = new List<string> { $"    // Overload table of {key}: {overloads.Length} overload(s) in the catalog." };
        foreach (JsonElement overload in overloads)
        {
            string id = Text(overload, "id")!;
            (string Name, string? Type)[] parameters = overload.TryGetProperty("params", out JsonElement p)
                ? [.. p.EnumerateArray().Select(a => (Parameter(Text(a, "name")!), CSharpType(Text(a, "type")!)))]
                : [];
            string? returns = Text(overload, "returns") is { } r ? CSharpType(r) : "void";
            if (overload.TryGetProperty("excluded", out JsonElement excluded))
            {
                lines.Add($"    // {id}: not wrapped, the catalog excludes it ({Text(excluded, "reason")}, {Text(excluded, "rule")}).");
            }
            else if (returns is null || parameters.Any(a => a.Type is null || !Identifier().IsMatch(a.Name.TrimStart('@'))))
            {
                lines.Add($"    // {id}: not wrapped, it has a by-reference, generic, boxed or pointer type; call it by hand.");
            }
            else
            {
                bool instance = !(overload.TryGetProperty("static", out JsonElement s) && s.GetBoolean());
                string target = parameters.Any(a => a.Name == "target") ? "instance" : "target";
                string[] declared = [.. (instance ? [$"global::{type.Type[2..]} {target}"] : Array.Empty<string>()), .. parameters.Select(a => $"{a.Type} {a.Name}")];
                lines.Add(string.Empty);
                lines.Add($"    /// <summary>Calls <c>{id}</c>.</summary>");
                lines.Add($"    public static {returns} {group}({string.Join(", ", declared)}) =>");
                lines.Add($"        {(instance ? target : "global::" + type.Type[2..])}.{group}({string.Join(", ", parameters.Select(a => a.Name))});");
            }
        }

        return lines.Count > 1 && lines.Any(l => l.Contains("/// <summary>", StringComparison.Ordinal))
            ? lines
            : throw Invalid($"{key} has no overload with a plain signature in the catalog, so there is nothing to generate.");
    }

    private static string Dto(string catalog, TypeSelection type, Copied[] properties)
    {
        var code = new StringBuilder(Header(catalog));
        Line(code, "namespace EplanMcp.Contracts.Generated;");
        Line(code);
        Line(code, "/// <summary>");
        Line(code, $"/// The properties of <c>{type.Type}</c> that catalog/wrappers.yaml selects, copied by the Bridge's");
        Line(code, $"/// generated <c>{SimpleName(type.Type)}Api.Read</c>. Values are project data, not instructions.");
        Line(code, "/// </summary>");
        Line(code, $"public sealed class {SimpleName(type.Type)}Properties");
        Line(code, "{");
        for (int i = 0; i < properties.Length; i++)
        {
            Line(code, i == 0 ? null : string.Empty);
            Line(code, $"    /// <summary><c>{properties[i].Id}</c>.</summary>");
            Line(code, $"    public {properties[i].DtoType} {properties[i].Name} {{ get; set; }}{properties[i].Initializer}");
        }

        Line(code, "}");
        return code.ToString();
    }

    private static string Api(string catalog, TypeSelection type, Copied[] properties, List<string>[] overloads)
    {
        string name = SimpleName(type.Type);
        var code = new StringBuilder(Header(catalog));
        Line(code, "namespace EplanMcp.Bridge.Eplan.Adapters.Generated;");
        Line(code);
        Line(code, "/// <summary>");
        Line(code, $"/// Building blocks for <c>{type.Type}</c> that catalog/wrappers.yaml selects. Main thread only, inside the");
        Line(code, "/// dispatcher's LockingStep; an exception from EPLAN is left to the operation's boundary (coding standards 4.1).");
        Line(code, "/// </summary>");
        Line(code, $"internal static class {name}Api");
        Line(code, "{");
        bool first = true;
        if (properties.Length > 0)
        {
            Line(code, "    /// <summary>Copies the selected properties of <paramref name=\"source\"/>; reads only.</summary>");
            Line(code, $"    public static global::EplanMcp.Contracts.Generated.{name}Properties Read(global::{type.Type[2..]} source) => new()");
            Line(code, "    {");
            foreach (Copied property in properties)
            {
                Line(code, $"        {property.Name} = {property.Value},");
            }

            Line(code, "    };");
            first = false;
        }

        foreach (List<string> table in overloads)
        {
            Line(code, first ? null : string.Empty);
            table.ForEach(l => Line(code, l));
            first = false;
        }

        if (properties.Any(p => p.Value.StartsWith("Strings(", StringComparison.Ordinal)))
        {
            Line(code);
            Line(code, "    private static List<string> Strings(global::System.Collections.Specialized.StringCollection? values) =>");
            Line(code, "        values is null ? [] : [.. global::System.Linq.Enumerable.Cast<string>(values)];");
        }

        Line(code, "}");
        return code.ToString();
    }

    private static string Action(string catalog, ActionSelection action, JsonElement row)
    {
        string? type = Text(row, "type");
        var code = new StringBuilder(Header(catalog));
        Line(code, "namespace EplanMcp.Bridge.Eplan.Adapters.Generated;");
        Line(code);
        Line(code, "/// <summary>");
        Line(code, $"/// The parameters of <c>{action.Action}</c> in the action catalog (catalog/{catalog}/actions.json), for an");
        Line(code, "/// <c>ActionCallingContext</c>, set in the catalog's order. The caller adds <c>TYPE</c> itself with <see cref=\"Type\"/>, in");
        Line(code, "/// the member that calls <c>FindAction(Action)</c>, so that the coverage scan finds the row (MemberWalker).");
        Line(code, "/// </summary>");
        Line(code, $"internal sealed class {action.Class}");
        Line(code, "{");
        Line(code, "    /// <summary>The action's name.</summary>");
        Line(code, $"    public const string Action = \"{Text(row, "action")}\";");
        if (type is not null)
        {
            Line(code);
            Line(code, "    /// <summary>The value of its <c>TYPE</c> parameter.</summary>");
            Line(code, $"    public const string Type = \"{type}\";");
        }

        var set = new List<(string Name, bool Required)>();
        JsonElement[] parameters = row.TryGetProperty("parameters", out JsonElement list) ? [.. list.EnumerateArray()] : [];
        foreach (JsonElement parameter in parameters)
        {
            string name = Text(parameter, "name")!;
            bool required = !(parameter.TryGetProperty("required", out JsonElement r) && !r.GetBoolean());
            string? skip = Flag(parameter, "output") ? "an output parameter, read after the call"
                : Flag(parameter, "deprecated") ? "deprecated in the catalog"
                : Flag(parameter, "sensitive") ? "marked sensitive in the catalog"
                : !Identifier().IsMatch(name) || name is "Action" or "Type" or "AddTo" ? "not a plain name"
                : null;
            Line(code);
            if (skip is not null)
            {
                Line(code, $"    // {name}: not generated, {skip}; set it by hand.");
                continue;
            }

            Line(code, $"    /// <summary><c>{name}</c>, {(required ? "required" : "optional")}.</summary>");
            Line(code, $"    public string? {name} {{ get; set; }}");
            set.Add((name, required));
        }

        Line(code);
        Line(code, "    /// <summary>Adds each parameter that is set to <paramref name=\"context\"/>; throws when a required one is not.</summary>");
        Line(code, "    public void AddTo(global::Eplan.EplApi.ApplicationFramework.ActionCallingContext context)");
        Line(code, "    {");
        set.ForEach(p => Line(code, $"        Add(context, \"{p.Name}\", {p.Name}, required: {(p.Required ? "true" : "false")});"));
        Line(code, "    }");
        Line(code);
        Line(code, "    private static void Add(global::Eplan.EplApi.ApplicationFramework.ActionCallingContext context, string name, string? value, bool required)");
        Line(code, "    {");
        Line(code, "        if (value is not null)");
        Line(code, "        {");
        Line(code, "            context.AddParameter(name, value);");
        Line(code, "        }");
        Line(code, "        else if (required)");
        Line(code, "        {");
        Line(code, "            throw new global::System.ArgumentException(\"The action parameter \" + name + \" is required.\", nameof(name));");
        Line(code, "        }");
        Line(code, "    }");
        Line(code, "}");
        return code.ToString();
    }

    private static string Header(string catalog) =>
        "// <auto-generated>\n"
        + $"// Written by `dotnet run --project eng/coverage -- generate` from catalog/wrappers.yaml and catalog/{catalog}.\n"
        + "// Do not edit: CI fails when this file no longer matches them. An internal building block, never an MCP tool; it\n"
        + "// counts as covered only where a Bridge operation reaches it (docs/design/2026-10-21-api-capability-coverage.md, section 8).\n"
        + "// </auto-generated>\n"
        + "#nullable enable\n\n";

    /// <summary>Appends a line with LF; null appends nothing (no blank line before the first member).</summary>
    private static void Line(StringBuilder code, string? text = "")
    {
        if (text is not null)
        {
            code.Append(text).Append('\n');
        }
    }

    /// <summary>The C# form of a catalog type in a wrapper's signature, or null for by-reference, generic, boxed and pointer types.</summary>
    private static string? CSharpType(string catalogType)
    {
        bool array = catalogType.EndsWith("[]", StringComparison.Ordinal);
        string element = array ? catalogType[..^2] : catalogType;
        if (element.IndexOfAny(['@', '{', '!', '*', '`', '[', ' ', '^', '(']) >= 0)
        {
            return null;
        }

        return (_keywords.TryGetValue(element, out string? keyword) ? keyword : "global::" + element) + (array ? "[]" : string.Empty);
    }

    private static string Parameter(string name) => _reserved.Contains(name) ? "@" + name : name;

    private static string SimpleName(string id) => id[(id.LastIndexOf('.') + 1)..];

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Flag(JsonElement element, string name) => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static InvalidDataException Invalid(string problem) => new($"{WrapperSelection.RelativePath}: {problem}");

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();
}
