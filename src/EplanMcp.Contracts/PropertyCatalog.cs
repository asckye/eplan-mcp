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

    /// <summary>Exact object/ID lookup; matching an ID on another object type grants nothing.</summary>
    public static PropertyDictionaryEntry? Find(string objectType, int propertyId) =>
        _byId.TryGetValue((objectType, propertyId), out PropertyDictionaryEntry? entry) ? entry : null;

    /// <summary>Exact, case-sensitive formal-name lookup; no localized, fuzzy or custom identifiers.</summary>
    public static PropertyDictionaryEntry? Find(string objectType, string propertyName) =>
        _byName.TryGetValue((objectType, propertyName), out PropertyDictionaryEntry? entry) ? entry : null;

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
