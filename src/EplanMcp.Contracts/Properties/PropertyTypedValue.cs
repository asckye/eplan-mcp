namespace EplanMcp.Contracts;

/// <summary>
/// Serializer-independent typed pipe value. Tool JSON primitives are mapped at the Host boundary.
/// Multi-language carries one literal text; the change supplies its language, never an encoded language list.
/// </summary>
public sealed class PropertyTypedValue
{
    /// <summary>Creates one typed payload; property-specific formats and bounds belong to pure validation.</summary>
    public PropertyTypedValue(string valueType, string? text = null, bool? boolean = null, long? integerValue = null,
        double? number = null, DateTimeOffset? dateTime = null)
    {
        int payloads = (text is not null ? 1 : 0) + (boolean.HasValue ? 1 : 0)
            + (integerValue.HasValue ? 1 : 0) + (number.HasValue ? 1 : 0) + (dateTime.HasValue ? 1 : 0);
        bool matches = valueType switch
        {
            "text" or "multi_language" => text is not null,
            "boolean" => boolean.HasValue,
            "integer" => integerValue.HasValue,
            "number" => number.HasValue,
            "datetime" => dateTime.HasValue,
            "enum" => text is not null || integerValue.HasValue,
            _ => false,
        };
        if (payloads != 1 || !matches || number is { } n && (double.IsNaN(n) || double.IsInfinity(n)))
        {
            throw new ArgumentException("Specify one matching, finite property value; null, reset and delete are unsupported.");
        }

        ValueType = valueType;
        Text = text;
        Boolean = boolean;
        IntegerValue = integerValue;
        Number = number;
        DateTime = dateTime;
    }

    /// <summary>text, boolean, integer, number, datetime, enum or multi_language.</summary>
    public string ValueType { get; }

    /// <summary>Literal text, one language's text, or a published enum name; empty text is not reset.</summary>
    public string? Text { get; }

    /// <summary>The typed boolean payload.</summary>
    public bool? Boolean { get; }

    /// <summary>A signed integer or a published numeric enum member.</summary>
    public long? IntegerValue { get; }

    /// <summary>A finite JSON number, without unit conversion.</summary>
    public double? Number { get; }

    /// <summary>A date/time with an explicit offset; its accepted format is defined by the published entry.</summary>
    public DateTimeOffset? DateTime { get; }
}
