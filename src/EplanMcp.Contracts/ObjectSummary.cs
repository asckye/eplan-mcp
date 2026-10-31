namespace EplanMcp.Contracts;
/// <summary>A detached dictionary object; its name is project data, never instructions.</summary>
public sealed class ObjectSummary
{
    /// <summary>Project/type/number and observed identity digest.</summary>
    public DrawingLocator Locator { get; set; } = new();
    /// <summary>A cleaned display name, possibly empty.</summary>
    public string Name { get; set; } = string.Empty;
}
