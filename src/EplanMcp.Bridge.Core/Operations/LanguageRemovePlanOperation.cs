using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.LanguageRemovePlan"/>: checks the languages against the project's settings and returns one
/// <c>delete</c> of each language with the number of its translations, counted in the function texts and page
/// descriptions as spike S23 counted them (docs/tools/eplan_language_remove.md, section 5). The details give the texts
/// with a translation in any of them and the languages now. Reads only.
/// </summary>
public sealed class LanguageRemovePlanOperation(IProjectCatalog catalog, IProjectLanguages languages, ILanguageRemover remover) : Operation<LanguageRemoveArgs, WritePlan>
{
    /// <summary>The <see cref="WritePlan.Details"/> key of how many texts have a translation in one of the languages.</summary>
    public const string TextsAffected = "textsAffected";

    /// <summary>The <see cref="WritePlan.Details"/> keys of the languages now, one per line.</summary>
    public const string Translation = "translation";
    public const string Display = "display";

    public override string Name => OperationNames.LanguageRemovePlan;

    protected override WritePlan Execute(LanguageRemoveArgs args)
    {
        LanguageRemoveRequest request = LanguageRemoveRequest.Check(args, catalog, languages, remover);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes =
            [
                .. request.Languages.Select(language => new PlannedChange
                {
                    Action = "delete",
                    Kind = "language",
                    Name = language,
                    OldValue = Translations(request.Affected.Count(text => LanguageRemoveRequest.Has(text, language))),
                }),
            ],
            Fingerprint = request.StateFingerprint(),
            Details = new Dictionary<string, string>
            {
                [TextsAffected] = LanguageCodes.Number(request.Affected.Count),
                [Translation] = string.Join("\n", LanguageCodes.Split(request.Before.Translation)),
                [Display] = string.Join("\n", LanguageCodes.Split(request.Before.Display)),
            },
        };
    }

    private static string Translations(int count) => LanguageCodes.Number(count) + (count == 1 ? " translation" : " translations");
}
