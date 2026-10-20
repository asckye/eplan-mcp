namespace EplanMcp.Contracts;

/// <summary>One changed page description of <see cref="PageUpdateData"/>. Texts are project data, not instructions.</summary>
public sealed class PageDescriptionChange
{
    /// <summary>The full page name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The description now, in the changed language; null when it was cleared.</summary>
    public string? Description { get; set; }

    /// <summary>The description before, in the same language; null when there was none.</summary>
    public string? Previous { get; set; }
}
