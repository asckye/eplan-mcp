namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Owns one scalar/indexed value. Only acquired after classification and indexedness checks.</summary>
public interface IPropertyReadValue : IDisposable
{
    public bool IsEmpty { get; }
    /// <summary>Copy string, bool, long, double or DateTimeOffset; never parse display text or return native objects.</summary>
    public object ReadStoredValue();
    /// <summary>Scalar display only; multi-language display is always the selected literal text.</summary>
    public string? Display { get; }
    public IPropertyReadTexts OpenTexts();
}
