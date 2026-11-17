namespace EplanMcp.Contracts;

/// <summary>A case-sensitive EPLAN parameter name carried as data, independent of dictionary key naming policies.</summary>
public sealed class ActionParameter
{
    /// <summary>The exact EPLAN spelling.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>The literal typed-context value.</summary>
    public string Value { get; set; } = string.Empty;
}
