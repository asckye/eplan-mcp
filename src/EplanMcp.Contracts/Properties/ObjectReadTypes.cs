namespace EplanMcp.Contracts;

/// <summary>Catalog-derived object read support with compatible function query aliases (api-notes A60).</summary>
public static partial class ObjectReadTypes
{
    /// <summary>Additional query names; these do not introduce property dictionaries.</summary>
    public static IReadOnlyList<string> FunctionTypes { get; } = Array.AsReadOnly(new[] { "Terminal", "PLC", "BlackBox", "PLCTerminal" });
}
