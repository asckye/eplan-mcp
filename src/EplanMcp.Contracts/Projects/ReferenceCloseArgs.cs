namespace EplanMcp.Contracts;

/// <summary>Closes only a copy owned by this Bridge, never an arbitrary project path.</summary>
public sealed class ReferenceCloseArgs
{
    /// <summary>The handle returned by reference.prepare.</summary>
    public string Handle { get; set; } = string.Empty;
}
