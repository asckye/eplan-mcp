namespace EplanMcp.Bridge.Core.Operations;

/// <summary>One cable function's session identity, full name, nullable automatic flag and business snapshot hash.</summary>
public sealed class CableGenerationCable(string id, string name, bool? automatic, string business)
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    public bool? Automatic { get; } = automatic;

    public string Business { get; } = business;
}
