namespace EplanMcp.Contracts;

/// <summary>One literal assignment for planning; this DTO grants no execution permission.</summary>
public sealed class PropertySetChange
{
    /// <summary>The exact business address or checked locator.</summary>
    public PropertyTarget Target { get; set; } = null!;

    /// <summary>The numeric ID or formal dictionary name, with an explicit index where required.</summary>
    public PropertySelector Property { get; set; } = null!;

    /// <summary>The requested typed value; null/reset/delete are unsupported.</summary>
    public PropertyTypedValue Value { get; set; } = null!;

    /// <summary>The existing project language to edit; never a serialized language list.</summary>
    public string? Language { get; set; }
}
