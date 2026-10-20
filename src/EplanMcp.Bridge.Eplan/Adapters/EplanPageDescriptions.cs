using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads and writes page descriptions (<c>PAGE_NOMINATIOMN</c>) language by language. Reading takes the stored
/// languages from the raw value (<c>ToString()</c>, api-notes A11) and each text with <c>GetString</c>; writing assigns
/// a new <see cref="MultiLangString"/> with one <c>AddString</c> per language, which replaces the whole value as S9
/// observed (A13). The language-independent entry <c>??_??</c> is <c>ISOCode.Language.L___</c>, which EPLAN's API
/// documentation names for language-independent strings. Main thread; writes inside the write's TransactionScope.
/// </summary>
internal sealed class EplanPageDescriptions : IPageDescriptions
{
    public string? LanguageCode(string? language)
    {
        if (language is null)
        {
            using var languages = new Languages();
            return languages.GuiLanguage.GetString();
        }

        using var iso = new ISOCode(language);
        return iso.IsValid() ? language : null;
    }

    public PageDescription Read(string projectPath, string pageName)
    {
        MainThread.Assert();
        PropertyValue value = Find(projectPath, pageName).Properties.PAGE_NOMINATIOMN;
        if (value.IsEmpty)
        {
            return new PageDescription(null, []);
        }

        string raw = value.ToString();
        using MultiLangString text = value.ToMultiLangString();
        var entries = new List<TextEntry>();
        foreach (string code in TextLanguages.Stored(raw))
        {
            if (Number(code) is { } number)
            {
                entries.Add(new TextEntry(code, text.GetString(number)));
            }
        }

        return new PageDescription(raw, entries);
    }

    public PageDescription Write(string projectPath, string pageName, IReadOnlyList<TextEntry> entries)
    {
        MainThread.Assert();
        using var text = new MultiLangString();
        foreach (TextEntry entry in entries)
        {
            text.AddString(Number(entry.Language) ?? throw new InvalidOperationException("Unknown language " + entry.Language), entry.Text);
        }

        Find(projectPath, pageName).Properties.PAGE_NOMINATIOMN = text;
        return Read(projectPath, pageName);
    }

    /// <summary>The language's number; null for a code EPLAN does not know (a <c>;xx_YY@</c> inside a text).</summary>
    private static ISOCode.Language? Number(string code)
    {
        if (code == TextLanguages.Independent)
        {
            return ISOCode.Language.L___;
        }

        using var iso = new ISOCode(code);
        return iso.IsValid() ? iso.GetNumber() : null;
    }

    /// <summary>The page with exactly this name, as page.get finds it (S6: the exact name filter, checked ordinally).</summary>
    private static Page Find(string projectPath, string pageName)
    {
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        using var filter = new PagesFilter { Name = pageName, ExactNameMatching = true };
        return finder.GetPages(filter).FirstOrDefault(p => string.Equals(p.Name, pageName, StringComparison.Ordinal))
            ?? throw new BridgeErrorException(
                ErrorReason.PageNotFound,
                "The page is no longer in the project. Call eplan_page_list to see its pages.",
                retryable: false,
                new Dictionary<string, string> { ["page"] = pageName });
    }
}
