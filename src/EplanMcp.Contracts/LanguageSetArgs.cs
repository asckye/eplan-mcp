namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.LanguageSetPlan"/> and <see cref="OperationNames.LanguageSetExecute"/>.</summary>
public sealed class LanguageSetArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>
    /// The languages the pages show, in order, 1 to 10 codes such as <c>en_US</c>, each a translation language of the
    /// project or in <see cref="Add"/>; <c>["##_##"]</c> alone clears them. Null leaves them as they are.
    /// </summary>
    public List<string>? Display { get; set; }

    /// <summary>1 to 10 languages to add to the translation languages; those the project has are skipped. Null adds none.</summary>
    public List<string>? Add { get; set; }

    /// <summary>
    /// The project's source language, the one its texts are written in, such as <c>de_DE</c>: a translation language of
    /// the project or in <see cref="Add"/>; <c>##_##</c> clears it. Null leaves it as it is.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>For the execute operation: the plan's <see cref="WritePlan.Fingerprint"/>. Null for the plan.</summary>
    public string? Fingerprint { get; set; }
}
