namespace EplanMcp.Packs;

/// <summary>
/// JSON Schema keywords <see cref="PackSchema"/> adds to a property: <c>pattern</c> (on the items of a list),
/// <c>minimum</c>, <c>const</c>. The validator enforces the same rules in code.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class SchemaAttribute : Attribute
{
    public string? Pattern { get; init; }

    /// <summary><see cref="long.MinValue"/>: none.</summary>
    public long Minimum { get; init; } = long.MinValue;

    /// <summary><see cref="int.MinValue"/>: none.</summary>
    public int Const { get; init; } = int.MinValue;
}
