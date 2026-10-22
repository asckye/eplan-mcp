namespace EplanMcp.Contracts;

/// <summary>
/// A project's languages as its settings store them (api-notes A28): <c>TRANSLATEGUI.TRANSLATE_LANGUAGES</c>,
/// <c>TRANSLATEGUI.DISPLAYED_LANGUAGES</c>, each in stored order, and <c>TRANSLATEGUI.SOURCE_LANGUAGE</c>. <c>##_##</c>
/// stands for "not set".
/// </summary>
public sealed class ProjectLanguages
{
    /// <summary>The project's translation languages, e.g. <c>de_DE</c>, <c>en_US</c>.</summary>
    public List<string> Translation { get; set; } = [];

    /// <summary>The languages the pages show, in order; <c>["##_##"]</c> when none are set.</summary>
    public List<string> Display { get; set; } = [];

    /// <summary>The source language, the one the texts are written in; <c>##_##</c> when not set, null from a Bridge that does not read it.</summary>
    public string? Source { get; set; }
}
