using System.Globalization;
using System.Text.RegularExpressions;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Pure input checks against verified entries; validating a candidate never authorizes a write.</summary>
public static class PropertyValueValidation
{
    /// <summary>Only shipped exact keys grant permission; callers cannot supply candidate authorization data.</summary>
    public static PropertyWriteEntry? ShippedEntry(int version, string objectType, int id, string category,
        string path, ICollection<PropertyViolation> violations)
    {
        if (PropertyCatalog.ReadRefusal(objectType, id) is null
            && PropertyWriteWhitelist.Entries.TryGetValue((version, objectType, id, category), out PropertyWriteEntry? entry))
        {
            return entry;
        }

        Add(violations, path, "This property write is not in the shipped whitelist.", ErrorReason.PolicyDenied);
        return null;
    }

    /// <summary>Checks a tool JSON primitive before DTO construction, without coercing strings or losing integer precision.</summary>
    public static PropertyTypedValue? Value(JToken? input, PropertyWriteEntry entry, string path, ICollection<PropertyViolation> violations)
    {
        int before = violations.Count;
        PropertyDefinitionMetadata definition = entry.Definition;
        string? text = input?.Type == JTokenType.String ? input.Value<string>() : null;
        if (text is not null)
        {
            if (text.Length > 500 || definition.MaximumLength is { } length && text.Length > length)
            {
                Add(violations, path, "Text exceeds the product or published length limit.");
            }

            if (text.Length == 0 && definition.EmptyAllowed != true)
            {
                Add(violations, path, "Empty text is not explicitly allowed.");
            }

            if (definition.MaximumLength is null or < 0)
            {
                Add(violations, path, "A verified text length limit is required.", ErrorReason.PolicyDenied);
            }
        }

        long? integer = Integer(input);
        double number = 0;
        DateTimeOffset dateTime = default;
        bool matches = definition.ValueType switch
        {
            "text" or "multi_language" => text is not null,
            "boolean" => input?.Type == JTokenType.Boolean,
            "integer" => integer.HasValue,
            "number" => input?.Type is JTokenType.Integer or JTokenType.Float
                && double.TryParse(input.ToString(Formatting.None), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                && !double.IsNaN(number) && !double.IsInfinity(number),
            "datetime" => text is not null && definition.Format is { Length: > 0 } format
                && Regex.IsMatch(text, @"(?:Z|[+-][0-9]{2}:[0-9]{2})\z", RegexOptions.CultureInvariant)
                && DateTimeOffset.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime),
            "enum" => text is not null || integer.HasValue,
            _ => false,
        };
        if (!matches)
        {
            Add(violations, path, "Supply the published JSON value type and format; null, reset and nonfinite numbers are unsupported.");
            return null;
        }

        var value = definition.ValueType switch
        {
            "boolean" => new PropertyTypedValue("boolean", boolean: input!.Value<bool>()),
            "integer" => new PropertyTypedValue("integer", integerValue: integer),
            "number" => new PropertyTypedValue("number", number: number),
            "datetime" => new PropertyTypedValue("datetime", dateTime: dateTime),
            "enum" => new PropertyTypedValue("enum", text: text, integerValue: integer),
            _ => new PropertyTypedValue(definition.ValueType!, text: text),
        };
        if (value.ValueType is "integer" or "number")
        {
            Bounds(value, definition, path, violations);
        }

        if (value.ValueType == "enum")
        {
            List<PropertyTypedValue>? members = definition.EnumValues;
            if (members is null || members.Count == 0 || members.Any(m => m.ValueType != "enum")
                || members.Any(m => (m.Text is not null) != (members[0].Text is not null)))
            {
                Add(violations, path, "One verified enum representation is required.", ErrorReason.PolicyDenied);
            }
            else if (!members.Any(m => m.Text == value.Text && m.IntegerValue == value.IntegerValue))
            {
                Add(violations, path, "Use a published enum member in its published representation.");
            }
        }

        return violations.Count == before ? value : null;
    }

    /// <summary>
    /// Checks property.index and language from the original change. Unit is resolved storage metadata, not a new tool field.
    /// Empty unit means unitless; null is unknown. Unit errors point to value. The later planner checks project languages.
    /// </summary>
    public static void Qualifiers(JToken? index, JToken? language, string? storageUnit, PropertyWriteEntry entry,
        string changePath, ICollection<PropertyViolation> violations)
    {
        PropertyDefinitionMetadata definition = entry.Definition;
        string at = changePath + "/property/index";
        if (definition.IsIndexed is null || definition.IsIndexed == true
            && (definition.MinimumIndex is null || definition.MaximumIndex is null || definition.MinimumIndex > definition.MaximumIndex))
        {
            Add(violations, at, "Verified index constraints are required.", ErrorReason.PolicyDenied);
        }
        else if (definition.IsIndexed == true && (Integer(index) is not { } i
            || i < definition.MinimumIndex || i > definition.MaximumIndex)
            || definition.IsIndexed == false && index is not null)
        {
            Add(violations, at, "Supply an explicit index in the published range only for indexed properties.");
        }

        if (definition.Unit is null || storageUnit is null)
        {
            Add(violations, changePath + "/value", "Verified storage unit metadata is required.", ErrorReason.PolicyDenied);
        }
        else if (!string.Equals(definition.Unit, storageUnit, StringComparison.Ordinal))
        {
            Add(violations, changePath + "/value", "The storage unit must match the published unit; conversion is unsupported.");
        }

        bool multilingual = definition.ValueType == "multi_language";
        if (definition.LanguageMode != (multilingual ? "explicit_merge_preserve" : "none"))
        {
            Add(violations, changePath + "/language", "A supported verified language mode is required.", ErrorReason.PolicyDenied);
        }
        else if (multilingual && (language?.Type != JTokenType.String
            || !Regex.IsMatch(language.Value<string>()!, @"\A[a-z]{2}_[A-Z]{2}\z", RegexOptions.CultureInvariant))
            || !multilingual && language is not null)
        {
            Add(violations, changePath + "/language", "Supply an explicit EPLAN ISO language only for multi-language values.");
        }
    }

    private static void Bounds(PropertyTypedValue value, PropertyDefinitionMetadata definition, string path, ICollection<PropertyViolation> violations)
    {
        PropertyTypedValue? lower = definition.LowerBound;
        PropertyTypedValue? upper = definition.UpperBound;
        if (lower?.ValueType != value.ValueType || upper?.ValueType != value.ValueType || Compare(lower, upper) > 0)
        {
            Add(violations, path, "Verified matching inclusive numeric bounds are required.", ErrorReason.PolicyDenied);
        }
        else if (Compare(value, lower) < 0 || Compare(value, upper) > 0)
        {
            Add(violations, path, "The value is outside the published inclusive numeric bounds.");
        }
    }

    private static int Compare(PropertyTypedValue left, PropertyTypedValue right) => left.ValueType == "integer"
        ? left.IntegerValue!.Value.CompareTo(right.IntegerValue!.Value)
        : left.Number!.Value.CompareTo(right.Number!.Value);

    private static long? Integer(JToken? token) => token?.Type == JTokenType.Integer
        && long.TryParse(token.ToString(Formatting.None), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : null;

    private static void Add(ICollection<PropertyViolation> violations, string path, string message, string reason = ErrorReason.InvalidArgument)
    {
        violations.Add(new PropertyViolation
        {
            Path = path,
            Reason = reason,
            Message = message,
        });
    }
}
