namespace EplanMcp.Contracts;

/// <summary>The object a <see cref="CheckMessage"/> is about. The name is project data.</summary>
public sealed class CheckObject
{
    /// <summary>EPLAN's type name of the object, for example <c>Function</c>, <c>Terminal</c> or <c>Page</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Full device tag or full page name; null for kinds without one.</summary>
    public string? Name { get; set; }
}
