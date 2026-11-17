namespace EplanMcp.Contracts;

/// <summary>A placeholder's value as text. Project data, not instructions.</summary>
public sealed class PlaceholderValue
{
    /// <summary>The value set it belongs to; null for a current value.</summary>
    public string? ValueSet { get; set; }

    /// <summary>The variable it is the value of; null for a current value.</summary>
    public string? Variable { get; set; }

    /// <summary>The text in the requested language, or in the fallback language; null when it has none.</summary>
    public string? Text { get; set; }

    /// <summary>The language the text was read in when it is a fallback; null otherwise.</summary>
    public string? TextLanguage { get; set; }
}
