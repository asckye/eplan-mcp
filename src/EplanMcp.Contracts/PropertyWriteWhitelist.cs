using System.Collections.ObjectModel;

namespace EplanMcp.Contracts;

/// <summary>Shipped authorization data only; candidates stay absent until every scratch gate passes.</summary>
public static class PropertyWriteWhitelist
{
    /// <summary>Version of the planning rules; included in every property plan fingerprint.</summary>
    public const string PlanningVersion = "b1-func-text/1";

    /// <summary>Planning-only seam. A44 leaves 20027 opaque; no candidate grants executable write permission.</summary>
    public static PropertyWriteEntry? PlanningCandidate(int version, string objectType, int id) =>
        (version, objectType, id) == (2026, "Function", 20011) ? new PropertyWriteEntry
        {
            EplanMajorVersion = version,
            ObjectType = objectType,
            PropertyId = id,
            PropertyName = "FUNC_TEXT",
            Definition = new PropertyDefinitionMetadata
            {
                ValueType = "multi_language",
                MaximumLength = 500,
                EmptyAllowed = true,
                IsIndexed = false,
                Unit = string.Empty,
                LanguageMode = "explicit_merge_preserve",
            },
            NormalizedComparison = "ordinal_per_language",
            SideEffectGuards = ["same_tag_functions", "identity", "unchanged_translations", "parts"],
            ApiNotesEvidence = ["A36", "A42", "A44"],
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
