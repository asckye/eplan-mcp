using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace EplanMcp.Catalog;

/// <summary>
/// Writes the catalog deterministically: lines sorted by ID with ordinal comparison, fields in a fixed order, no
/// timestamps or machine paths, LF line ends. Running twice on the same SDK gives identical files. Enum values are kept
/// in their type's line and property IDs in data-dictionary.jsonl, so the member files stay small.
/// </summary>
internal static class CatalogWriter
{
    private static readonly JsonWriterOptions _line = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonWriterOptions _indented = _line with { Indented = true, NewLine = "\n" };

    public static void Write(CatalogData catalog, string output)
    {
        string members = Path.Combine(output, "members");
        if (Directory.Exists(members))
        {
            Directory.Delete(members, recursive: true);
        }

        Directory.CreateDirectory(members);
        ILookup<string?, CatalogEntry> enumValues = catalog.Entries
            .Where(e => e.Kind == "enum_value" && e.Layer == Classifier.TypeLayer).ToLookup(e => e.DeclaringType);
        WriteLines(Path.Combine(output, "types.jsonl"), catalog.Entries.Where(IsType).Select(e => Json(_line, w => WriteEntry(w, e, enumValues[e.Id]))));
        foreach (IGrouping<string, CatalogEntry> ns in catalog.Entries
            .Where(e => !IsType(e) && e.Kind != "enum_value" && e.Layer != Classifier.DataDictionary).GroupBy(e => e.Namespace))
        {
            WriteLines(Path.Combine(members, ns.Key + ".jsonl"), ns.Select(e => Json(_line, w => WriteEntry(w, e, []))));
        }

        WriteLines(Path.Combine(output, "data-dictionary.jsonl"), DataDictionary.Build(catalog.Entries).Select(p => Json(_line, w =>
        {
            w.WriteStartObject();
            w.WriteString("object", p.Object);
            w.WriteString("name", p.Name);
            Optional(w, "value", p.Value);
            Flag(w, "enum", p.EnumType is null, value: false);
            Flag(w, "list", p.ListType is null, value: false);
            Flag(w, "indexed", !p.Indexed, value: false);
            Flag(w, "obsolete", p.Obsolete);
            Flag(w, "documented", !p.Documented, value: false);
            w.WriteEndObject();
        })));
        WriteLines(Path.Combine(output, "assemblies.json"), [Json(_indented, w => WriteAssemblies(w, catalog.Assemblies))]);
        WriteLines(Path.Combine(output, "summary.json"), [Json(_indented, w => Summary.Write(w, catalog))]);
    }

    internal static string Json(JsonWriterOptions options, Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, options))
        {
            write(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    internal static string Json(CatalogEntry entry) => Json(_line, w => WriteEntry(w, entry, []));

    private static bool IsType(CatalogEntry e) => e.Id.StartsWith("T:", StringComparison.Ordinal);

    private static void WriteLines(string path, IEnumerable<string> lines) =>
        File.WriteAllText(path, string.Concat(lines.Select(l => l + "\n")), new UTF8Encoding(false));

    private static void WriteEntry(Utf8JsonWriter w, CatalogEntry e, IEnumerable<CatalogEntry> values)
    {
        w.WriteStartObject();
        w.WriteString("id", e.Id);
        w.WriteString("kind", e.Kind);
        w.WriteString("layer", e.Layer);
        if (IsType(e))
        {
            w.WriteString("asm", e.Assembly);
            w.WriteString("ns", e.Namespace);
            Optional(w, "outer", e.DeclaringType);
        }
        else
        {
            w.WriteString("type", e.DeclaringType);
        }

        Optional(w, "group", e.Group);
        Optional(w, "access", e.Access);
        Flag(w, "static", e.Static);
        Optional(w, "returns", e.Returns is "System.Void" ? null : e.Returns);
        if (e.Parameters is { } parameters)
        {
            w.WriteStartArray("params");
            foreach (Parameter p in parameters)
            {
                w.WriteStartObject();
                w.WriteString("name", p.Name);
                w.WriteString("type", p.Type);
                w.WriteEndObject();
            }

            w.WriteEndArray();
        }

        Optional(w, "accessors", e.Accessors);
        Optional(w, "setterAccess", e.SetterAccess);
        Optional(w, "value", e.Value);
        Optional(w, "base", e.Base);
        if (e.Interfaces is { Count: > 0 } interfaces)
        {
            w.WriteStartArray("interfaces");
            interfaces.ToList().ForEach(w.WriteStringValue);
            w.WriteEndArray();
        }

        Optional(w, "modifiers", e.Modifiers);
        if (values.Any())
        {
            w.WriteStartObject("values");
            values.ToList().ForEach(v => w.WriteString(v.Name, v.Value));
            w.WriteEndObject();
        }

        Flag(w, "documented", !e.Documented, value: false);
        Flag(w, "obsolete", e.Obsolete);
        if (e.Excluded is { } excluded)
        {
            w.WriteStartObject("excluded");
            w.WriteString("reason", excluded.Reason);
            w.WriteString("rule", excluded.Rule);
            w.WriteEndObject();
        }

        w.WriteEndObject();
    }

    private static void WriteAssemblies(Utf8JsonWriter w, List<AssemblyInfo> assemblies)
    {
        w.WriteStartArray();
        foreach (AssemblyInfo a in assemblies)
        {
            w.WriteStartObject();
            w.WriteString("file", a.File);
            Optional(w, "name", a.Name);
            Optional(w, "assemblyVersion", a.Version);
            Optional(w, "fileVersion", a.FileVersion);
            Optional(w, "intellisense", a.Xml);
            w.WriteNumber("entries", a.Entries);
            Optional(w, "skipped", a.Skipped);
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    private static void Optional(Utf8JsonWriter w, string name, string? value)
    {
        if (value is not null)
        {
            w.WriteString(name, value);
        }
    }

    // Writes a boolean only when it differs from the schema default.
    private static void Flag(Utf8JsonWriter w, string name, bool write, bool value = true)
    {
        if (write)
        {
            w.WriteBoolean(name, value);
        }
    }
}
