using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.TranslationKeywordSearch"/> (docs/tools/eplan_translation_keyword.md): reads the keywords
/// of the configured translation database whose source text matches the query, sorts them by source text (ordinal, so
/// pages stay stable) and slices, all in one main-thread call. A read: the database is never changed.
/// </summary>
public sealed class TranslationKeywordSearchOperation(ITranslationDatabase database)
    : Operation<TranslationKeywordArgs, TranslationKeywordData>
{
    public const int MinQueryLength = 1;
    public const int MaxQueryLength = 200;

    public override string Name => OperationNames.TranslationKeywordSearch;

    protected override TranslationKeywordData Execute(TranslationKeywordArgs args)
    {
        string query = Given(args.Query) ?? string.Empty;
        if (query.Length is < MinQueryLength or > MaxQueryLength)
        {
            throw InvalidArgument(string.Format(
                CultureInfo.InvariantCulture,
                "'query' must have {0} to {1} characters: text a keyword's source text contains.",
                MinQueryLength,
                MaxQueryLength));
        }

        TranslationDatabaseContent content = database.Read(query);
        // The adapter searches with EPLAN's own matching, which an exact request narrows again here rather than in the
        // adapter: the same read then serves both, and the narrowing is testable without EPLAN.
        List<TranslationKeywordItem> matching =
        [
            .. content.Keywords
                .Where(k => !args.Exact || string.Equals(k.Source.Trim(), query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k.Source, StringComparer.Ordinal),
        ];
        return new TranslationKeywordData
        {
            Database = PartSearchOperation.ForModel(content.Database),
            Languages = [.. content.Languages.OrderBy(l => l, StringComparer.Ordinal)],
            Items = Paging.Slice(matching, args.Offset, args.Limit),
            Total = matching.Count,
        };
    }
}
