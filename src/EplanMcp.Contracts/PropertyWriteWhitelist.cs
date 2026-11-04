using System.Collections.ObjectModel;

namespace EplanMcp.Contracts;

/// <summary>Host authorization data; B1 text and category-scoped B8 PLC assignments.</summary>
public static class PropertyWriteWhitelist
{
    /// <summary>Version of the planning rules; included in every property plan fingerprint.</summary>
    public const string PlanningVersion = "b8-plc/1";

    /// <summary>B1 planning/execution constraints (A44/A48), shared with the published Host whitelist.</summary>
    public static PropertyWriteEntry? PlanningCandidate(int version, string objectType, int id, string category = "") =>
        version == 2026 && objectType == "Function" && id is 20011 or 20027 ? new PropertyWriteEntry
        {
            EplanMajorVersion = version,
            ObjectType = objectType,
            PropertyId = id,
            PropertyName = id == 20011 ? "FUNC_TEXT" : "FUNC_TECHNICAL_CHARACTERISTIC",
            Definition = new PropertyDefinitionMetadata
            {
                ValueType = id == 20011 ? "multi_language" : "text",
                MaximumLength = 500,
                EmptyAllowed = true,
                IsIndexed = false,
                Unit = string.Empty,
                LanguageMode = id == 20011 ? "explicit_merge_preserve" : "none",
            },
            NormalizedComparison = id == 20011 ? "ordinal_per_language" : "ordinal_text",
            SideEffectGuards = ["same_tag_functions", "identity", "unchanged_translations", "parts"],
            ApiNotesEvidence = ["A36", "A42", "A44", "A48"],
        } : version == 2026 && objectType == "Function" ? PlcEntries().FirstOrDefault(e => e.PropertyId == id
            && (category.Length == 0 || e.ApplicableCategory == category)) : null;

    /// <summary>Exact version/object/ID/category keys. No tool, configuration or local data can add entries.</summary>
    public static IReadOnlyDictionary<
        (int EplanMajorVersion, string ObjectType, int PropertyId, string ApplicableCategory),
        PropertyWriteEntry> Entries
    {
        get;
    } =
        new ReadOnlyDictionary<(int, string, int, string), PropertyWriteEntry>(
            new[] { 20011, 20027 }.Select(id => PlanningCandidate(2026, "Function", id)!).Concat(PlcEntries())
                .ToDictionary(e => (e.EplanMajorVersion, e.ObjectType, e.PropertyId, e.ApplicableCategory)));
    private static IEnumerable<PropertyWriteEntry> PlcEntries()
    {
        foreach (string category in new[] { "PLCBox", "PLCTerminal", "BusTerminal" })
        {
            foreach (int id in category == "PLCBox" ? new[] { 20161, 20408, 20409, 20410, 20411, 20427, 20308 } : new[] { 20400, 20402, 20407, 20011 })
            {
                PropertyWriteEntry entry = id == 20011 ? PlanningCandidate(2026, "Function", id)! : new PropertyWriteEntry
                {
                    EplanMajorVersion = 2026,
                    ObjectType = "Function",
                    PropertyId = id,
                    PropertyName = PropertyCatalog.Find("Function", id)!.PropertyName,
                    Definition = new PropertyDefinitionMetadata
                    {
                        ValueType = id == 20308 ? "integer" : "text",
                        MaximumLength = 500,
                        EmptyAllowed = true,
                        IsIndexed = false,
                        Unit = "",
                        LanguageMode = "none",
                        LowerBound = id == 20308 ? new("integer", integerValue: 0) : null,
                        UpperBound = id == 20308 ? new("integer", integerValue: 51) : null,
                    },
                    NormalizedComparison = "ordinal_stored_value",
                    ApiNotesEvidence = ["A90", "A91", "B13"],
                    SideEffectGuards = ["same_tag_functions", "identity", "parts"],
                };
                entry.ApplicableCategory = category;
                yield return entry;
            }
        }
    }
}
