using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Storage comparisons only: no trimming, language-list parsing, culture guessing or display conversion.</summary>
public static class PropertyValueComparison
{
    public static bool Equal(PropertyTypedValue? left, PropertyTypedValue? right) =>
        left is null || right is null ? left == right : left.ValueType == right.ValueType
            && string.Equals(left.Text, right.Text, StringComparison.Ordinal)
            && left.Boolean == right.Boolean && left.IntegerValue == right.IntegerValue
            && left.Number == right.Number && left.DateTime == right.DateTime;

    /// <summary>Missing/empty language text compares as empty; other languages are never used as fallback.</summary>
    public static string TextIn(IEnumerable<SnapshotText> texts, string language) =>
        texts.SingleOrDefault(t => string.Equals(t.Language, language, StringComparison.Ordinal))?.Text ?? string.Empty;
}
