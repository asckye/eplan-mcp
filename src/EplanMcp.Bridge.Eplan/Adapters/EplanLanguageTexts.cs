using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// The multi-language texts spike S23 compared (api-notes A28), for eplan_language_remove and eplan_project_translate:
/// the stored values of every function's <c>FUNC_TEXT</c> and every page's <c>PAGE_NOMINATIOMN</c>
/// (<c>de_DE@…;sv_SE@…;</c>, A11), keyed by kind and session identifier (<c>F …</c>, <c>P …</c>) and named. A property
/// that is not set is left out, since <c>ToString()</c> on it throws (coding standards 9.5, rule 13). Main thread,
/// inside the dispatcher's LockingStep.
/// </summary>
internal static class EplanLanguageTexts
{
    /// <summary>Every function's text, then every page's description; S23 read all 497 texts of the sample project so.</summary>
    public static List<LanguageText> All(DMObjectsFinder finder)
    {
        var texts = new List<LanguageText>();
        foreach (Function function in finder.GetFunctions(null))
        {
            Add(texts, function);
        }

        foreach (Page page in finder.GetPages(null))
        {
            Add(texts, page);
        }

        return texts;
    }

    /// <summary>For each page, the texts of the functions on it (<c>FunctionsFilter.Page</c>, S6), then its description.</summary>
    public static List<LanguageText> OnPages(DMObjectsFinder finder, IEnumerable<Page> pages)
    {
        var texts = new List<LanguageText>();
        foreach (Page page in pages)
        {
            foreach (Function function in FunctionsOn(finder, page))
            {
                Add(texts, function);
            }

            Add(texts, page);
        }

        return texts;
    }

    /// <summary>The functions on <paramref name="page"/>, through a filter of their own, not the shared <c>Page.Filter</c>.</summary>
    public static Function[] FunctionsOn(DMObjectsFinder finder, Page page)
    {
        using var filter = new FunctionsFilter { Page = page };
        return finder.GetFunctions(filter);
    }

    private static void Add(List<LanguageText> texts, Function function) => Add(texts, "F " + function.ToStringIdentifier(), function.Name ?? string.Empty, function.Properties.FUNC_TEXT);

    private static void Add(List<LanguageText> texts, Page page) => Add(texts, "P " + page.ToStringIdentifier(), page.Name ?? string.Empty, page.Properties.PAGE_NOMINATIOMN);

    private static void Add(List<LanguageText> texts, string id, string name, PropertyValue value)
    {
        if (!value.IsEmpty)
        {
            texts.Add(new LanguageText { Id = id, Name = name, Raw = value.ToString() ?? string.Empty });
        }
    }
}
