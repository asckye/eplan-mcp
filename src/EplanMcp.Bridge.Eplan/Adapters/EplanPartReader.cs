using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads the part references and the project's own part data for part.list. Main thread, inside the dispatcher's
/// LockingStep; the project is found by path (api-notes A11). Only the objects this class creates are disposed, as in
/// the rest of the adapter layer.
/// </summary>
internal sealed class EplanPartReader : IPartReader
{
    public ProjectParts ReadParts(string projectPath, string? language)
    {
        MainThread.Assert();
        ISOCode.Language textLanguage = EplanText.Language(language);
        Project project = EplanProjects.ByPath(projectPath);
        using var finder = new DMObjectsFinder(project);
        return new ProjectParts
        {
            // A null filter returns every valid reference (Count > 0, part number set; EPLAN XML docs): S6 found 2900 in
            // the sample project, references on wires included, against 981 on functions.
            References = [.. finder.GetArticleReferences(null).Select(Reference)],

            // The project's part data (S6: 293 in 10 ms), never the parts database: Article.LoadFromMasterdata would write
            // into the project (BannedSymbols.txt). Descriptions are multi-language, the manufacturer is not (S6).
            Articles =
            [
                .. project.Articles.Select(article => new ArticleRecord
                {
                    PartNumber = article.PartNr ?? string.Empty,
                    Variant = article.VariantNr ?? string.Empty,
                    Description = EplanText.Read(article.Properties.ARTICLE_DESCR1, textLanguage),
                    Manufacturer = EplanText.Read(article.Properties.ARTICLE_MANUFACTURER, textLanguage),
                }),
            ],
        };
    }

    /// <summary>ParentObject is the project, a function or a connection (EPLAN XML docs); S6 saw connections.</summary>
    private static PartReferenceRecord Reference(ArticleReference reference)
    {
        StorableObject? owner = reference.ParentObject;
        return new PartReferenceRecord
        {
            PartNumber = reference.PartNr ?? string.Empty,
            Variant = reference.VariantNr ?? string.Empty,
            Count = reference.Count,
            Owner = owner switch
            {
                Function => PartOwner.Function,
                Connection => PartOwner.Connection,
                _ => PartOwner.Other,
            },
            OwnerName = owner is Function function ? function.Name ?? string.Empty : string.Empty,
        };
    }
}
