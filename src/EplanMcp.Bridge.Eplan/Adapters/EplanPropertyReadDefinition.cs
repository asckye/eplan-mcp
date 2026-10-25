using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>Copies instance definition facts; unsupported facts remain unknown (design 4.4; A42 Large gate).</summary>
internal sealed class EplanPropertyReadDefinition(PropertyDefinition definition, Action check) : IPropertyReadDefinition
{
    public PropertyDefinitionMetadata Metadata
    {
        get
        {
            PropertyDefinition.PropertyType? type = Fact(() => definition.Type);
            string? valueType = type.HasValue ? ValueType(type) : null;
            bool? internalProperty = Fact(() => definition.IsInternal);
            return new PropertyDefinitionMetadata
            {
                ValueType = valueType,
                IsInternal = internalProperty,
                IsReadOnly = Fact(() => definition.IsReadOnly),
                IsIndexed = Fact(() => definition.IsIndexed),
                MaximumIndex = Fact(() => definition.MaxIndex),
                IsNamePart = Fact(() => definition.IsNamePart),
                IsPropertyLicensed = Fact(() => definition.IsPropertyLicensed),
                LowerBound = internalProperty == false ? Bound(() => definition.LowerBound, valueType) : null,
                UpperBound = internalProperty == false ? Bound(() => definition.UpperBound, valueType) : null,
            };
        }
    }

    private T? Fact<T>(Func<T> read) where T : struct
    {
        check();
        try
        {
            return read();
        }
        catch (BaseException)
        {
            return null; // The API cannot supply this fact; unknown never grants permission.
        }
    }

    private PropertyTypedValue? Bound(Func<PropertyValue> read, string? type)
    {
        check();
        try
        {
            using PropertyValue value = read();
            check();
            if (value.IsEmpty)
            {
                return null;
            }

            check();
            double number = type == "number" ? value.ToDouble() : 0;
            return type switch
            {
                "boolean" => new(type, boolean: value.ToBool()),
                "integer" => new(type, integerValue: value.ToInt()),
                "number" => double.IsNaN(number) || double.IsInfinity(number) ? null : new(type, number: number),
                _ => null,
            };
        }
        catch (BaseException)
        {
            return null; // Missing or unsupported bounds remain unknown.
        }
    }

    internal static string ValueType(PropertyDefinition.PropertyType? type) => type switch
    {
        PropertyDefinition.PropertyType.String => "text",
        PropertyDefinition.PropertyType.MultilangString => "multi_language",
        PropertyDefinition.PropertyType.Bool => "boolean",
        PropertyDefinition.PropertyType.Long => "integer",
        PropertyDefinition.PropertyType.Double or PropertyDefinition.PropertyType.Coord => "number",
        _ => "opaque", // Time zones, enums and ValueWithUnit need per-batch evidence; never parse display.
    };

    public void Dispose() => definition.Dispose();
}
