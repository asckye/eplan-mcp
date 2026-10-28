namespace Eplan.EplApi.Base;

// Test-only API surface: a linked production adapter runs against borrowed objects whose disposal fails.
internal readonly record struct PointD(double X, double Y);

internal sealed class BaseException : Exception;

internal sealed class MultiLangString(string content) : IDisposable
{
    public bool Disposed { get; private set; }
    public string GetAsString() => content;
    private KeyValuePair<string, string>[] Translations => [.. content.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(v => v.Split('@', 2)).Select(v => KeyValuePair.Create(v[0], v[1]))];
    public int GetLanguageList(ref LanguageList languages)
    {
        languages.Codes = [.. Translations.Select(t => t.Key)];
        return languages.Codes.Length;
    }
    public string GetString(string language) => Translations.Single(t => t.Key == language).Value;
    public void Dispose() => Disposed = true;
}

internal sealed class LanguageList
{
    public string[] Codes { get; set; } = [];
    public string get_Language(int index) => Codes[index];
}

internal sealed class ISOCode(string language) : IDisposable
{
    public string GetString() => language;
    public void Dispose() { }
}
