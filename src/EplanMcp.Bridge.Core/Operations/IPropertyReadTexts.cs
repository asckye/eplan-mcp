namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Owns MultiLangString; copy stored language codes and literal texts, including ??_??, without fallback.</summary>
public interface IPropertyReadTexts : IDisposable
{
    public IReadOnlyList<string> Languages { get; }
    public string GetText(string language);
}
