using System.Text.RegularExpressions;

namespace EplanMcp.Catalog;

/// <summary>
/// Puts each entry in a counting layer, groups overloads, and applies the mechanical exclusion rules
/// (docs/design/2026-10-21-api-capability-coverage.md section 3). Every other status is curated, never derived here.
/// </summary>
internal static partial class Classifier
{
    public const string Operation = "operation";
    public const string DataDictionary = "data_dictionary";
    public const string TypeLayer = "type";

    private const string PropertyListSuffix = "PropertyList";

    public static void Classify(CatalogEntry entry)
    {
        string typeName = entry.DeclaringType is { } declaring ? declaring[(declaring.LastIndexOf('.') + 1)..] : "";
        if (entry.Kind == "property" && typeName.EndsWith(PropertyListSuffix, StringComparison.Ordinal)
            && PropertyIdName().IsMatch(entry.Name) && entry.Parameters is null or [{ Type: "System.Int32" }])
        {
            // FUNC_TEXT and its indexed overload FUNC_TEXT(System.Int32) are one property ID of the Function object.
            entry.Layer = DataDictionary;
            entry.Object = typeName[..^PropertyListSuffix.Length];
        }
        else if (entry.Kind == "enum_value" && entry.DeclaringType![..^(typeName.Length + 1)].EndsWith(".Properties", StringComparison.Ordinal))
        {
            // Enums nested in a Properties class (Eplan.EplApi.DataModel.Properties.Function) carry the numeric IDs.
            entry.Layer = DataDictionary;
            entry.Object = typeName;
        }
        else
        {
            // Object properties are operations too: moving, rotating or renaming an object is a property setter.
            entry.Layer = entry.Kind is "method" or "property" or "operator" or "delegate_method" ? Operation : TypeLayer;
        }

        if (entry.Layer == Operation)
        {
            entry.Group = "O:" + entry.DeclaringType![2..] + "." + entry.Name;
        }

        entry.Excluded = Exclude(entry);
    }

    private static Exclusion? Exclude(CatalogEntry entry) => entry switch
    {
        { NativeSignature: true } => new("internal", "native_signature"),
        _ when entry.Namespace.Split('.').Contains("Internal", StringComparer.Ordinal) => new("internal", "internal_namespace"),
        { Obsolete: true } => new("deprecated", "obsolete_attribute"),
        { Kind: "operator" } => new("not_applicable", "operator"),
        { Kind: "delegate_method" } => new("not_applicable", "delegate_member"),
        { Kind: "method", Name: "ToString" or "GetHashCode", Parameters: null } => new("not_applicable", "object_override"),
        { Kind: "method", Name: "Equals", Parameters: [{ Type: "System.Object" }] } => new("not_applicable", "object_override"),
        _ => null,
    };

    [GeneratedRegex("^[A-Z][A-Z0-9_]*$")]
    private static partial Regex PropertyIdName();
}
