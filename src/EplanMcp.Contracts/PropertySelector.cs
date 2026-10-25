namespace EplanMcp.Contracts;

/// <summary>An ID or formal dictionary name, optionally indexed; resolution belongs to pure validation.</summary>
public sealed class PropertySelector
{
    /// <summary>Creates exactly one selector branch; does not resolve names or validate applicability.</summary>
    public PropertySelector(int? propertyId = null, string? propertyName = null, int? index = null)
    {
        if (propertyId.HasValue == (propertyName is not null))
        {
            throw new ArgumentException("Specify exactly one property ID or formal name.");
        }

        PropertyId = propertyId;
        PropertyName = propertyName;
        Index = index;
    }

    /// <summary>The dictionary ID; the same ID on another object type grants no permission.</summary>
    public int? PropertyId { get; }

    /// <summary>The exact symbolic dictionary name, never a localized or fuzzy name.</summary>
    public string? PropertyName { get; }

    /// <summary>An explicit EPLAN index; null leaves index selection to the operation.</summary>
    public int? Index { get; }
}
