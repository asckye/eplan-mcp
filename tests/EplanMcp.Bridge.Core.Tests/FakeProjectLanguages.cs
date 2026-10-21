using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests;

/// <summary>
/// A project's language settings, changed the way S23 found EPLAN changes them (api-notes A28): SetDisplayedLanguages
/// takes codes separated by commas and refuses semicolons, the settings are stored as <c>en_US;zh_CN;</c>, and nothing
/// here is taken back by a rollback. <see cref="Failure"/> is thrown by the change <see cref="FailOn"/> names;
/// <see cref="KeepDisplay"/> and <see cref="IgnoreWrite"/> stand for EPLAN accepting a call but not changing the
/// setting; <see cref="WriteFailure"/> for a write-back EPLAN refuses. Records every call.
/// </summary>
internal sealed class FakeProjectLanguages : IProjectLanguages
{
    public LanguageSettings Settings { get; set; } = new() { Translation = "de_DE;en_US;zh_CN;", Display = "##_##;", Source = "##_##" };

    public List<string> Calls { get; } = [];

    public Exception? Failure { get; set; }

    /// <summary>The call <see cref="Failure"/> is thrown by, e.g. "SetDisplayed"; null for the first change.</summary>
    public string? FailOn { get; set; }

    public bool KeepDisplay { get; set; }

    public bool IgnoreWrite { get; set; }

    public Exception? WriteFailure { get; set; }

    /// <summary>Runs after each change, e.g. to cancel the invoke meanwhile.</summary>
    public Action<string>? OnChange { get; set; }

    public LanguageSettings Read(string projectPath) =>
        new() { Translation = Settings.Translation, Display = Settings.Display, Source = Settings.Source };

    public void Add(string projectPath, string language)
    {
        Change("Add " + language);
        if (!LanguageCodes.Split(Settings.Translation).Contains(language))
        {
            Settings.Translation += language + ";";
        }
    }

    public void SetDisplayed(string projectPath, string languages)
    {
        Change("SetDisplayed " + languages);
        if (languages.Contains(';', StringComparison.Ordinal))
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN refused the displayed languages.");
        }

        if (!KeepDisplay)
        {
            Settings.Display = LanguageCodes.Stored(languages.Split(','));
        }
    }

    public void Write(string projectPath, LanguageSettings settings)
    {
        Calls.Add("Write " + settings.Translation + " " + settings.Display);
        if (WriteFailure is not null)
        {
            throw WriteFailure;
        }

        if (!IgnoreWrite)
        {
            Settings.Translation = settings.Translation;
            Settings.Display = settings.Display;
        }
    }

    private void Change(string call)
    {
        Calls.Add(call);
        if (Failure is not null && (FailOn is null || call.StartsWith(FailOn, StringComparison.Ordinal)))
        {
            throw Failure;
        }

        OnChange?.Invoke(call);
    }
}
