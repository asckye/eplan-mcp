namespace EplanMcp.Contracts;

/// <summary>A verified reference resource and a new empty scratch destination, supplied only by the Host.</summary>
public sealed class ReferencePrepareArgs
{
    /// <summary>The closed source .elk or project backup .zw1; never opened or closed.</summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>The new copy's .elk file in the Host's empty folder.</summary>
    public string Destination { get; set; } = string.Empty;
    /// <summary>The configured scratch root containing the destination.</summary>
    public string ScratchRoot { get; set; } = string.Empty;
}
