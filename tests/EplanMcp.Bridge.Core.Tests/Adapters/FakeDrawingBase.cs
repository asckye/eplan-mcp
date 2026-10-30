namespace Eplan.EplApi.Base;

// Test-only API surface: a linked production adapter runs against borrowed objects whose disposal fails.
internal readonly record struct PointD(double X, double Y);

internal sealed class BaseException : Exception;

internal sealed class MultiLangString(string content = "") : IDisposable
{
    private readonly Dictionary<ISOCode.Language, string> _entries = content.Split(';', StringSplitOptions.RemoveEmptyEntries)
        .Select(s => s.Split('@', 2)).ToDictionary(p => new ISOCode(p[0]).GetNumber(), p => p[1]);
    public bool Disposed { get; private set; }
    public string GetAsString() => string.Concat(_entries.Select(p => new ISOCode(p.Key).GetString() + "@" + p.Value + ";"));
    public void AddString(ISOCode.Language language, string text) => _entries[language] = text;
    public string GetStringToDisplay(ISOCode.Language language) => _entries.GetValueOrDefault(language, _entries.GetValueOrDefault(ISOCode.Language.L___, ""));
    public string GetString(ISOCode.Language language) => _entries[language];
    public int GetLanguageList(ref LanguageList languages) { languages.Values = [.. _entries.Keys]; return _entries.Count; }
    public void Dispose() => Disposed = true;
}

internal sealed class ISOCode : IDisposable
{
    private readonly string _language;
    public ISOCode(string language) => _language = language;
    public ISOCode(Language language) => _language = language switch { Language.L___ => "??_??", Language.en_US => "en_US", _ => language.ToString() };
    public bool IsValid() => _language is "en_US" or "de_DE" or "zh_CN";
    public string GetString() => _language;
    public Language GetNumber() => _language == "??_??" ? Language.L___ : Enum.Parse<Language>(_language);
    public void Dispose() { }
    public enum Language { en_US, de_DE, zh_CN, L___, L_en_US = en_US }
}

internal sealed class LanguageList
{
    public ISOCode.Language[] Values { get; set; } = [];
    public ISOCode.Language get_Language(int index) => Values[index];
}

internal readonly record struct RectangleD(PointD LowerLeft, PointD UpperRight);

internal sealed class Languages : IDisposable
{
    public ISOCode GuiLanguage { get; } = new("zh_CN");
    public void Dispose() => GuiLanguage.Dispose();
}
