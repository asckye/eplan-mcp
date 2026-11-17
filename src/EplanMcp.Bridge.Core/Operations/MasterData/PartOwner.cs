namespace EplanMcp.Bridge.Core.Operations;

/// <summary>What a part reference belongs to (<c>ArticleReference.ParentObject</c>, EPLAN XML docs: project, function or connection).</summary>
public enum PartOwner
{
    /// <summary>Neither a function nor a connection, for example the project itself.</summary>
    Other,

    /// <summary>A function: a device, or one of its auxiliary functions.</summary>
    Function,

    /// <summary>A connection: S6 found part references on wires.</summary>
    Connection,
}
