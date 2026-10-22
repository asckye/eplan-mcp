using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.ProjectTranslatePlan"/>: checks the project's source and translation languages, reads the
/// function texts and page descriptions in the scope and returns one <c>update</c> of the project's
/// <c>translations</c>, worded like "the project: 37 of 497 texts miss a translation language; from de_DE into 19
/// languages" (docs/tools/eplan_project_translate.md, section 5). No text with a text in the source language misses a
/// translation language: no change, so nobody is asked. Which texts the translation database fills in is known only
/// after running. Reads only.
/// </summary>
public sealed class ProjectTranslatePlanOperation(IProjectCatalog catalog, IPageReader pages, IProjectLanguages languages, IProjectTranslator translator)
    : Operation<ProjectTranslateArgs, WritePlan>
{
    /// <summary>The <see cref="WritePlan.Details"/> keys: the source language, the translation languages (one per line), the counts.</summary>
    public const string Source = "source";
    public const string Languages = "languages";
    public const string TextsChecked = "textsChecked";
    public const string TextsMissing = "textsMissing";

    public override string Name => OperationNames.ProjectTranslatePlan;

    protected override WritePlan Execute(ProjectTranslateArgs args)
    {
        ProjectTranslateRequest request = ProjectTranslateRequest.Check(args, catalog, pages, languages, translator);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = request.Candidates.Count == 0
                ? []
                : [new PlannedChange { Action = "update", Kind = "project", Name = request.Project.Name, Property = "translations", OldValue = request.Scope() }],
            Fingerprint = request.StateFingerprint(),
            Details = new Dictionary<string, string>
            {
                [Source] = request.Source,
                [Languages] = string.Join("\n", request.Languages),
                [TextsChecked] = request.Texts.Count.ToString(CultureInfo.InvariantCulture),
                [TextsMissing] = request.Candidates.Count.ToString(CultureInfo.InvariantCulture),
            },
        };
    }
}
