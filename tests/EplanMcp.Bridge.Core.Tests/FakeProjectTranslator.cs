using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests;

/// <summary>
/// A project's texts by page, translated the way S23 r2 found TranslateObjects translates (api-notes A28): from the
/// source language of <paramref name="languages"/>, and only when it is set, every text with a text in it gets the
/// translations <see cref="Dictionary"/> holds for that text. <see cref="Snapshot"/> is for FakeTransactions: a rollback
/// restores the texts. <see cref="Failure"/> is thrown by the translation; <see cref="OnTranslate"/> runs after it.
/// Records every call.
/// </summary>
internal sealed class FakeProjectTranslator(FakeProjectLanguages languages) : IProjectTranslator
{
    /// <summary>The texts, each with the page it is on (the page's own description has its own name as page).</summary>
    public List<(string Page, LanguageText Text)> Stored { get; } = [];

    /// <summary>Source text → (language → translation), as the translation database holds it.</summary>
    public Dictionary<string, Dictionary<string, string>> Dictionary { get; } = new(StringComparer.Ordinal);

    public List<string> Calls { get; } = [];

    public Exception? Failure { get; set; }

    public bool Returns { get; set; } = true;

    public Action? OnTranslate { get; set; }

    public Func<Action> Snapshot => () =>
    {
        string[] raw = [.. Stored.Select(t => t.Text.Raw)];
        return () =>
        {
            for (int i = 0; i < raw.Length; i++)
            {
                Stored[i].Text.Raw = raw[i];
            }
        };
    };

    public void Add(string page, string id, string name, string raw) => Stored.Add((page, new LanguageText { Id = id, Name = name, Raw = raw }));

    public IReadOnlyList<LanguageText> Texts(string projectPath, IReadOnlyList<string>? pages)
    {
        Calls.Add("Texts " + (pages is null ? "project" : string.Join(",", pages)));
        return [.. InScope(pages).Select(t => new LanguageText { Id = t.Id, Name = t.Name, Raw = t.Raw })];
    }

    public bool Translate(string projectPath, IReadOnlyList<string>? pages)
    {
        Calls.Add("Translate " + (pages is null ? "project" : string.Join(",", pages)));
        if (Failure is not null)
        {
            throw Failure;
        }

        string source = LanguageCodes.SourceOf(languages.Settings);
        foreach (LanguageText text in InScope(pages))
        {
            if (source != LanguageCodes.Unset && ProjectTranslateRequest.TextIn(text.Raw, source) is { } original && Dictionary.TryGetValue(original, out Dictionary<string, string>? translations))
            {
                text.Raw += string.Concat(translations.Where(t => ProjectTranslateRequest.TextIn(text.Raw, t.Key) is null).Select(t => $"{t.Key}@{t.Value};"));
            }
        }

        OnTranslate?.Invoke();
        return Returns;
    }

    private IEnumerable<LanguageText> InScope(IReadOnlyList<string>? pages) =>
        Stored.Where(t => pages is null || pages.Contains(t.Page, StringComparer.Ordinal)).Select(t => t.Text);
}
