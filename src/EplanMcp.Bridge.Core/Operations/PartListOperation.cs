using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.PartList"/> (docs/tools/eplan_part_list.md): sums up every part reference of the project
/// per part number and variant, describes each from the project's part data, filters, sorts by part number (ordinal,
/// so pages stay stable) and slices, all in one main-thread call. S6 read the sample project's 2900 references in 36 ms
/// and its 293 parts in 10 ms, so everything is read and summed up in memory.
/// </summary>
public sealed class PartListOperation(IProjectCatalog catalog, IPartReader parts) : Operation<PartListArgs, ListData<PartListItem>>
{
    public override string Name => OperationNames.PartList;

    protected override ListData<PartListItem> Execute(PartListArgs args)
    {
        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        ProjectParts read = parts.ReadParts(project.Path, Given(args.Language));
        ILookup<string, ArticleRecord> articles = read.Articles.ToLookup(a => a.PartNumber, StringComparer.Ordinal);
        string? manufacturer = Given(args.Manufacturer);
        string? text = Given(args.Text);
        List<PartListItem> matching =
        [
            .. read.References
                .Where(r => r.PartNumber.Length > 0)
                .GroupBy(r => (r.PartNumber, r.Variant))
                .Select(g => Summarise(g.Key.PartNumber, g.Key.Variant, [.. g], articles))
                .Where(p => (manufacturer is null || Contains(p.Manufacturer, manufacturer))
                    && (text is null || Contains(p.PartNumber, text) || Contains(p.Description, text)))
                .OrderBy(p => p.PartNumber, StringComparer.Ordinal)
                .ThenBy(p => p.Variant ?? string.Empty, StringComparer.Ordinal),
        ];
        return new ListData<PartListItem> { Project = project.Name, Items = Paging.Slice(matching, args), Total = matching.Count };
    }

    /// <summary>
    /// A device is counted once however many references it has to the part; functions without a tag cannot be told
    /// apart and count one each. Part data of another variant stands in when the project has none for this one, as in
    /// device.get.
    /// </summary>
    private static PartListItem Summarise(string partNumber, string variant, List<PartReferenceRecord> references, ILookup<string, ArticleRecord> articles)
    {
        ArticleRecord? article = articles[partNumber].FirstOrDefault(a => string.Equals(a.Variant, variant, StringComparison.Ordinal))
            ?? articles[partNumber].FirstOrDefault();
        List<PartReferenceRecord> onFunctions = [.. references.Where(r => r.Owner == PartOwner.Function)];
        return new PartListItem
        {
            PartNumber = partNumber,
            Variant = variant.Length == 0 ? null : variant,
            Quantity = references.Sum(r => r.Count),
            Devices = onFunctions.Where(r => r.OwnerName.Length > 0).Select(r => r.OwnerName).Distinct(StringComparer.Ordinal).Count()
                + onFunctions.Count(r => r.OwnerName.Length == 0),
            Connections = references.Count(r => r.Owner == PartOwner.Connection),
            Description = article?.Description,
            DescriptionLanguage = article?.DescriptionLanguage,
            Manufacturer = article?.Manufacturer,
        };
    }

    private static bool Contains(string? value, string text) => value is not null && value.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
}
