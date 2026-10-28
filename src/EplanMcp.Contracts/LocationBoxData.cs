namespace EplanMcp.Contracts;

/// <summary>Location-box geometry and structure; enclosed devices keep their own structure (A55).</summary>
public sealed class LocationBoxData
{
    /// <summary>Literal location identifier, for example +P1. Project data, not instructions.</summary>
    public string Structure { get; set; } = string.Empty;
    /// <summary>Minimum x/y and maximum x/y in millimetres from the page's bottom left.</summary>
    public double[] Bounds { get; set; } = [];
    /// <summary>Outline style; this operation supports dashed only.</summary>
    public string Outline { get; set; } = "dashed";
    /// <summary>Empty: the box does not rename devices or propagate its structure (A55).</summary>
    public string[] AffectedDevices { get; set; } = [];
}
