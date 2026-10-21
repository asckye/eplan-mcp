using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.LanguageSetPlan"/>: reads the project's language settings and returns one <c>update</c> of
/// the project's <c>languages</c> with the settings that change before and after, as text such as
/// <c>display: ##_##</c> → <c>display: en_US, zh_CN</c>; no change when the settings are already as asked
/// (docs/tools/eplan_language_set.md, section 5). The details give both lists before and after. Reads only.
/// </summary>
public sealed class LanguageSetPlanOperation(IProjectCatalog catalog, IProjectLanguages languages) : Operation<LanguageSetArgs, WritePlan>
{
    /// <summary>The <see cref="WritePlan.Details"/> keys of the lists, one language per line.</summary>
    public const string TranslationBefore = "translationBefore";
    public const string DisplayBefore = "displayBefore";
    public const string TranslationAfter = "translationAfter";
    public const string DisplayAfter = "displayAfter";

    public override string Name => OperationNames.LanguageSetPlan;

    protected override WritePlan Execute(LanguageSetArgs args)
    {
        LanguageSetRequest request = LanguageSetRequest.Check(args, catalog, languages);
        List<string> translation = LanguageCodes.Split(request.Before.Translation);
        List<string> display = LanguageCodes.Split(request.Before.Display);
        IReadOnlyList<string> displayAfter = request.DisplayAfter();
        bool adds = request.ToAdd.Count > 0;
        bool shows = request.Display is not null && !request.Display.SequenceEqual(display, StringComparer.Ordinal);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = request.ChangesNothing()
                ? []
                :
                [
                    new PlannedChange
                    {
                        Action = "update",
                        Kind = "project",
                        Name = request.Project.Name,
                        Property = "languages",
                        OldValue = Text(adds ? translation : null, shows ? display : null),
                        NewValue = Text(adds ? request.Translation : null, shows ? displayAfter : null),
                    },
                ],
            Fingerprint = request.StateFingerprint(),
            Details = new Dictionary<string, string>
            {
                [TranslationBefore] = string.Join("\n", translation),
                [DisplayBefore] = string.Join("\n", display),
                [TranslationAfter] = string.Join("\n", request.Translation),
                [DisplayAfter] = string.Join("\n", displayAfter),
            },
        };
    }

    /// <summary>The settings that change, as the approval form and the audit show them: <c>translation: …; display: …</c>.</summary>
    private static string Text(IReadOnlyList<string>? translation, IReadOnlyList<string>? display) => string.Join(
        "; ",
        new[] { translation is null ? null : "translation: " + string.Join(", ", translation), display is null ? null : "display: " + string.Join(", ", display) }.OfType<string>());
}
