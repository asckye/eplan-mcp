using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Owns the native definition and bounds; Metadata copies facts only, unknown facts remain null (design 4.4).</summary>
public interface IPropertyReadDefinition : IDisposable
{
    public PropertyDefinitionMetadata Metadata { get; }
}
