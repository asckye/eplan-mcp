using System.Text.RegularExpressions;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests;

/// <summary>
/// A project's texts, and languages removed the way S23 found RemoveProjectLanguage removes one (api-notes A28): its
/// translations leave every text, it leaves the translation languages in <paramref name="languages"/>, and the displayed
/// languages become <see cref="DisplayAfter"/>. <see cref="Snapshot"/> is for FakeTransactions: a rollback restores the
/// texts only, never the settings. <see cref="Failure"/> is thrown by the removal of <see cref="FailOn"/> (null: the
/// first); <see cref="Keep"/> stands for a language EPLAN leaves in the list. Records every removal.
/// </summary>
internal sealed class FakeLanguageRemover(FakeProjectLanguages languages) : ILanguageRemover
{
    public List<LanguageText> Stored { get; } = [];

    public List<string> Removals { get; } = [];

    public string DisplayAfter { get; set; } = "zh_CN;";

    public Exception? Failure { get; set; }

    public string? FailOn { get; set; }

    public string? Keep { get; set; }

    /// <summary>Runs after each removal, e.g. to cancel the invoke meanwhile.</summary>
    public Action<string>? OnRemove { get; set; }

    public Func<Action> Snapshot => () =>
    {
        string[] raw = [.. Stored.Select(t => t.Raw)];
        return () =>
        {
            for (int i = 0; i < raw.Length; i++)
            {
                Stored[i].Raw = raw[i];
            }
        };
    };

    public IReadOnlyList<LanguageText> Texts(string projectPath) => [.. Stored.Select(t => new LanguageText { Id = t.Id, Raw = t.Raw })];

    public void Remove(string projectPath, string language)
    {
        Removals.Add(language);
        if (Failure is not null && (FailOn is null || FailOn == language))
        {
            throw Failure;
        }

        foreach (LanguageText text in Stored)
        {
            text.Raw = Regex.Replace(text.Raw, "(?<=^|;)" + language + "@[^;]*;", string.Empty);
        }

        if (language != Keep)
        {
            languages.Settings.Translation = LanguageCodes.Stored(LanguageCodes.Split(languages.Settings.Translation).Where(code => code != language));
            languages.Settings.Display = DisplayAfter;
        }

        OnRemove?.Invoke(language);
    }
}
