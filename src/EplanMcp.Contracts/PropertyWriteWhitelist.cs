using System.Collections.ObjectModel;

namespace EplanMcp.Contracts;

/// <summary>Shipped authorization data only; candidates stay absent until every scratch gate passes.</summary>
public static class PropertyWriteWhitelist
{
    /// <summary>Exact version/object/ID/category keys. No tool, configuration or local data can add entries.</summary>
    public static IReadOnlyDictionary<
        (int EplanMajorVersion, string ObjectType, int PropertyId, string ApplicableCategory),
        PropertyWriteEntry> Entries
    {
        get;
    } =
        new ReadOnlyDictionary<(int, string, int, string), PropertyWriteEntry>(
            new Dictionary<(int, string, int, string), PropertyWriteEntry>());
}
