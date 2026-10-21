using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.PartSearch"/> (docs/tools/eplan_part_search.md): reads every part of the configured parts
/// database, keeps those whose part number, type number, order number or description contains the query (ignoring
/// case), and of the manufacturer when one is given, sorts by part number and variant (ordinal, so pages stay stable)
/// and slices, all in one main-thread call. S24 read the 464 parts of the sample database in 0.07 s and filtered them
/// in under 0.01 s (api-notes A27). A read: the database is never changed.
/// </summary>
public sealed class PartSearchOperation(IPartsDatabase database) : Operation<PartSearchArgs, PartSearchData>
{
    public const int MinQueryLength = 2;
    public const int MaxQueryLength = 100;

    public override string Name => OperationNames.PartSearch;

    protected override PartSearchData Execute(PartSearchArgs args)
    {
        string query = Given(args.Query) ?? string.Empty;
        if (query.Length is < MinQueryLength or > MaxQueryLength)
        {
            throw InvalidArgument(string.Format(
                CultureInfo.InvariantCulture,
                "'query' must have {0} to {1} characters: text of a part number, type number, order number or description.",
                MinQueryLength,
                MaxQueryLength));
        }

        string? manufacturer = Given(args.Manufacturer);
        PartsDatabaseContent content = database.Read(Given(args.Language));
        List<PartSearchItem> matching =
        [
            .. content.Parts
                .Where(p => (manufacturer is null || string.Equals(p.Manufacturer?.Trim(), manufacturer, StringComparison.OrdinalIgnoreCase))
                    && (Contains(p.PartNumber, query) || Contains(p.TypeNumber, query) || Contains(p.OrderNumber, query) || Contains(p.Description, query)))
                .OrderBy(p => p.PartNumber, StringComparer.Ordinal)
                .ThenBy(p => p.Variant ?? string.Empty, StringComparer.Ordinal),
        ];
        return new PartSearchData { Database = ForModel(content.Database), Items = Paging.Slice(matching, args.Offset, args.Limit), Total = matching.Count };
    }

    /// <summary>
    /// The database as the model sees it (SEC-12): a file as EPLAN's setting names it, path variables unexpanded; of a
    /// SQL Server connection string (<c>Server|Database|0</c>, or <c>Server|Database|2|User|Password</c>, EPLAN XML docs
    /// of <c>MDPartsManagement.OpenDatabase</c>) only the server and the database, so a login never leaves the Bridge.
    /// </summary>
    public static string ForModel(string database)
    {
        string[] parts = database.Split('|');
        return parts.Length < 2 ? database : parts[0] + "|" + parts[1];
    }

    private static bool Contains(string? value, string text) => value is not null && value.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
}
