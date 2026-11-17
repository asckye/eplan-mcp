namespace EplanMcp.Contracts;

/// <summary>Catalog facts and shipped read classification only; no runtime facts or write permission.</summary>
public sealed class PropertyDictionaryEntry
{
    internal PropertyDictionaryEntry(string objectType, string propertyName, int? propertyId, bool obsolete, string? readRefusal)
    {
        ObjectType = objectType;
        PropertyName = propertyName;
        PropertyId = propertyId;
        Obsolete = obsolete;
        ReadRefusal = readRefusal;
    }

    /// <summary>The exact dictionary object type; no base/derived-type inheritance.</summary>
    public string ObjectType { get; }

    /// <summary>The exact symbolic name, never a localized display name.</summary>
    public string PropertyName { get; }

    /// <summary>The numeric ID, or null when the catalog has no public constant.</summary>
    public int? PropertyId { get; }

    /// <summary>Whether the catalog marks the definition obsolete.</summary>
    public bool Obsolete { get; }

    /// <summary>Shipped exclusion category, including unreviewed; null passes classification only, not runtime checks.</summary>
    public string? ReadRefusal { get; }
}
