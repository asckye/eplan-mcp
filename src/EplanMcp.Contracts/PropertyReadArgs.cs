namespace EplanMcp.Contracts;

/// <summary>One resolved object's read batch; continuation positions are internal, not public tool cursors.</summary>
public sealed class PropertyReadArgs
{
    /// <summary>The already resolved open project's path; never opens a project.</summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>The project object or an exact page full name.</summary>
    public PropertyTarget Target { get; set; } = null!;

    /// <summary>One to twenty numeric dictionary selectors, without formal names.</summary>
    public List<PropertySelector> Properties { get; set; } = [];

    /// <summary>An explicit language without fallback; null uses the GUI language and fallback.</summary>
    public string? Language { get; set; }

    /// <summary>Include cleaned display, translations and definition hints.</summary>
    public bool Detailed { get; set; }

    /// <summary>The next selector position; zero starts a batch.</summary>
    public int SelectorOffset { get; set; }

    /// <summary>The next existing-index position within that selector; never an assumed native index.</summary>
    public int IndexOffset { get; set; }
}
