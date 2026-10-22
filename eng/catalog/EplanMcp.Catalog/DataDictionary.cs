namespace EplanMcp.Catalog;

/// <summary>
/// One property ID of an object type, merged from its forms: the enum value <c>Properties.&lt;Object&gt;.&lt;NAME&gt;</c>
/// (which carries the numeric ID) and the <c>&lt;Object&gt;PropertyList.&lt;NAME&gt;</c> property with its indexed overload.
/// </summary>
internal sealed record DictionaryProperty(
    string Object, string Name, string? Value, string? EnumType, string? ListType, bool Indexed, bool Obsolete, bool Documented);

internal static class DataDictionary
{
    public static List<DictionaryProperty> Build(IEnumerable<CatalogEntry> entries) =>
    [
        .. entries.Where(e => e.Layer == Classifier.DataDictionary)
            .GroupBy(e => (e.Object!, e.Name))
            .Select(g =>
            {
                CatalogEntry? value = g.FirstOrDefault(e => e.Kind == "enum_value");
                CatalogEntry? list = g.FirstOrDefault(e => e.Kind == "property");
                return new DictionaryProperty(g.Key.Item1, g.Key.Name, value?.Value, value?.DeclaringType, list?.DeclaringType,
                    g.Any(e => e.Parameters is not null), g.Any(e => e.Obsolete), g.Any(e => e.Documented));
            })
            .OrderBy(p => p.Object, StringComparer.Ordinal).ThenBy(p => p.Name, StringComparer.Ordinal),
    ];
}
