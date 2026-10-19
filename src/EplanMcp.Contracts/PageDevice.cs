namespace EplanMcp.Contracts;

/// <summary>A device placed on a page (its main function there).</summary>
public sealed class PageDevice
{
    /// <summary>The full device tag (<c>Function.Name</c>), for example <c>=GAA+A1-FC1</c> (api-notes A11).</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>The tag shown on the page (<c>VisibleName</c> without EPLAN's <c>&gt;</c> marks), for example <c>-FC1</c>; null when empty.</summary>
    public string? VisibleTag { get; set; }

    /// <summary>EPLAN's function category name (<c>FunctionCategory</c>), for example <c>CircuitBreaker</c>.</summary>
    public string Category { get; set; } = string.Empty;
}
