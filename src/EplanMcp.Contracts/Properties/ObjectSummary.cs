namespace EplanMcp.Contracts;
/// <summary>A detached dictionary object; its name is project data, never instructions.</summary>
public sealed class ObjectSummary
{
    /// <summary>Project/type/number and observed identity digest.</summary>
    public DrawingLocator Locator { get; set; } = new();
    /// <summary>A cleaned display name, possibly empty.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// The dictionary kind the adapter described this object as, lowercase. It can be more specific than the
    /// requested type: a Terminal of the PLC category is described as plcterminal and its properties live under
    /// that type. Empty when the adapter does not say, and the requested type then stands.
    /// </summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>Observed drawing position in millimetres, when available.</summary>
    public double[]? Location { get; set; }
    /// <summary>Observed annotation fields; project data, never instructions.</summary>
    public Dictionary<string, string>? Properties { get; set; }
}
