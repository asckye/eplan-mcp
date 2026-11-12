using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Read access to the 3D mounting layout (api-notes A136), implemented by the EPLAN adapter; main thread only.</summary>
public interface ILayoutReader
{
    /// <summary>Spaces, placed objects and the schematic devices with parts of the open project at <paramref name="projectPath"/>.</summary>
    public LayoutRecord Read(string projectPath);
}

/// <summary>What <see cref="ILayoutReader"/> reads; Bridge.Core filters, orders and slices it.</summary>
public sealed class LayoutRecord
{
    /// <summary>Every installation space with its enclosures.</summary>
    public List<LayoutSpace> Spaces { get; set; } = [];

    /// <summary>Every placed 3D object, with the designation of its space.</summary>
    public List<LayoutObject> Objects { get; set; } = [];

    /// <summary>Main schematic functions with a part: tag and first part number, in EPLAN's order.</summary>
    public List<LayoutDevice> Devices { get; set; } = [];

    /// <summary>Names of every 3D function, placed or not; a device with one of these names already has a 3D representation.</summary>
    public HashSet<string> Names3D { get; set; } = new(StringComparer.Ordinal);
}
