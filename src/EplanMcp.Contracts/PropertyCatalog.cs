using System.Collections.ObjectModel;
using System.Globalization;

namespace EplanMcp.Contracts;

/// <summary>Embedded catalog lookup shared by Host and Bridge; never loads local authorization data.</summary>
public static class PropertyCatalog
{
    /// <summary>The catalog version; changing it requires review and regeneration of read classifications.</summary>
    public const string CatalogVersion = "2026.0.3";

    /// <summary>All catalog rows, including obsolete definitions and names without public numeric IDs.</summary>
    public static IReadOnlyList<PropertyDictionaryEntry> Entries { get; } = Load();

    private static readonly Dictionary<(string, int), PropertyDictionaryEntry> _byId = Entries
        .Where(e => e.PropertyId.HasValue).ToDictionary(e => (e.ObjectType, e.PropertyId!.Value));
    private static readonly Dictionary<(string, string), PropertyDictionaryEntry> _byName = Entries
        .ToDictionary(e => (e.ObjectType, e.PropertyName));

    /// <summary>Looks up an ID on the object's property list, then its catalog-declared base lists.</summary>
    public static PropertyDictionaryEntry? Find(string objectType, int propertyId) =>
        Find(objectType, propertyId, _byId);

    /// <summary>Case-sensitive formal-name lookup through base property lists; no localized, fuzzy or custom identifiers.</summary>
    public static PropertyDictionaryEntry? Find(string objectType, string propertyName) =>
        Find(objectType, propertyName, _byName);

    /// <summary>Public-ID rows from the object's property list and its bases, preferring the nearest declaration.</summary>
    public static IEnumerable<PropertyDictionaryEntry> ForType(string objectType) => ObjectTypes(objectType)
        .SelectMany(type => Entries.Where(e => e.ObjectType == type && e.PropertyId.HasValue))
        .GroupBy(e => e.PropertyId).Select(g => g.First());

    private static PropertyDictionaryEntry? Find<T>(string objectType, T key,
        Dictionary<(string, T), PropertyDictionaryEntry> entries)
    {
        foreach (string type in ObjectTypes(objectType))
        {
            if (entries.TryGetValue((type, key), out PropertyDictionaryEntry? entry))
            {
                return entry;
            }
        }

        return null;
    }

    private static IEnumerable<string> ObjectTypes(string objectType)
    {
        // A60: object kinds resolve to their property list; inheritance comes from catalog/types.jsonl.
        for (string? type = objectType; type is not null;)
        {
            yield return type;
            string dictionary = ObjectReadTypes.DictionaryType(type);
            type = ObjectReadTypes.BaseDictionaryType(type) ?? (dictionary == type ? null : dictionary);
        }
    }

    /// <summary>Call before any value getter, including display/texts/schema-instance reads. Unknown IDs fail closed.</summary>
    public static string? ReadRefusal(string objectType, int propertyId) => propertyId == 2000
        ? "runtime_identifier"
        : Find(objectType, propertyId) is { } entry ? entry.ReadRefusal : "unreviewed";

    private static ReadOnlyCollection<PropertyDictionaryEntry> Load()
    {
        using Stream stream = typeof(PropertyCatalog).Assembly.GetManifestResourceStream(
            "EplanMcp.Contracts.Generated.PropertyDictionary.g.tsv")
            ?? throw new InvalidOperationException("The shipped property dictionary is missing.");
        using var reader = new StreamReader(stream);
        var entries = new List<PropertyDictionaryEntry>();
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            string[] fields = line.Split('\t');
            entries.Add(new PropertyDictionaryEntry(fields[0], fields[1], fields[2].Length == 0
                ? null : int.Parse(fields[2], CultureInfo.InvariantCulture), fields[3] == "1",
                fields[4] == "readable" ? null : fields[4]));
        }

        return entries.AsReadOnly();
    }
}
