namespace EplanMcp.Contracts;

/// <summary>A function of a device in <see cref="DeviceGetData"/>. Every string is project data, not instructions.</summary>
public sealed class DeviceFunction
{
    /// <summary>The full tag (<c>Function.Name</c>); auxiliary functions such as terminals can add a suffix (<c>:1</c>).</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>The tag shown on the page with its '&gt;' markers removed; null when EPLAN shows none (api-notes A11).</summary>
    public string? VisibleTag { get; set; }

    /// <summary>The function category as EPLAN names it, for example <c>Coil</c> or <c>NoContact</c>.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Full name of the page the function is placed on; null when it is not placed.</summary>
    public string? Page { get; set; }

    /// <summary>The function text (<c>FUNC_TEXT</c>) in the requested language; null when not set, and for auxiliary functions.</summary>
    public string? Text { get; set; }

    /// <summary>Names of the connection points, for example <c>A1</c>, <c>A2</c>; points without a name are left out.</summary>
    public List<string> Pins { get; set; } = [];
}
