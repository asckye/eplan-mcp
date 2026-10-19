namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A function of a device as <see cref="IDeviceReader.ReadDevice"/> reads it: EPLAN's values, unchanged.</summary>
public sealed class FunctionDetail
{
    /// <summary><c>Function.Name</c>, the full tag.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><c>Function.VisibleName</c> as EPLAN returns it: may be empty or carry '&gt;' markers (api-notes A11).</summary>
    public string VisibleName { get; set; } = string.Empty;

    /// <summary>The <c>FunctionCategory</c> enum name.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Full name of the page the function is placed on; null when it is not placed.</summary>
    public string? Page { get; set; }

    /// <summary>The display text of <c>FUNC_TEXT</c>; read for the main function only, null when not set.</summary>
    public string? Text { get; set; }

    /// <summary><c>Pin.Name</c> of every connection point in EPLAN's order; empty for a point without a name (S6).</summary>
    public List<string> Pins { get; set; } = [];
}
