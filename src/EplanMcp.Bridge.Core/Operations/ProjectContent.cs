using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>What <see cref="IProjectReader"/> reads from a project; plain values, no EPLAN objects.</summary>
public sealed class ProjectContent
{
    /// <summary>The page type name of every page, in EPLAN's page order.</summary>
    public IReadOnlyList<string> PageTypes { get; set; } = [];

    /// <summary>Main functions with a name (<see cref="DeviceTags.IsDevice"/>).</summary>
    public int Devices { get; set; }

    public int Connections { get; set; }

    public int PartReferences { get; set; }

    /// <summary>Null unless the properties were asked for.</summary>
    public List<PropertyEntry>? Properties { get; set; }
}
