using System.Collections.ObjectModel;

namespace EplanMcp.Contracts;

/// <summary>Host authorization data; B1 Bridge candidates stay absent from Entries until every scratch gate passes.</summary>
public static class PropertyWriteWhitelist
{
    /// <summary>Version of the planning rules; included in every property plan fingerprint.</summary>
    public const string PlanningVersion = "b1-text/2";

    /// <summary>B1 Bridge planning/execution candidates (A44/A48); no candidate grants Host tool permission.</summary>
    public static PropertyWriteEntry? PlanningCandidate(int version, string objectType, int id) =>
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
        } : null;

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
