namespace EplanMcp.Bridge.Core.Operations;

/// <summary>One connection as <see cref="IConnectionReader"/> reads it: EPLAN's values, unchanged.</summary>
public sealed class ConnectionRecord
{
    /// <summary>True for a <c>Connection3D</c>: EPLAN throws on its ends and gives it no page (S6), so neither is read.</summary>
    public bool Is3D { get; set; }

    /// <summary><c>StartPin.ParentFunction.Name</c>; null when the connection has no start pin.</summary>
    public string? StartDevice { get; set; }

    /// <summary><c>StartPin.Name</c>; null when the connection has no start pin, empty when the pin has no name.</summary>
    public string? StartPin { get; set; }

    /// <summary><c>EndPin.ParentFunction.Name</c>; null when the connection has no end pin.</summary>
    public string? EndDevice { get; set; }

    /// <summary><c>EndPin.Name</c>; null when the connection has no end pin, empty when the pin has no name.</summary>
    public string? EndPin { get; set; }

    /// <summary>Full name of the page the connection is placed on; null when it is not placed.</summary>
    public string? Page { get; set; }

    /// <summary>The display text of <c>CONNECTION_WIRECROSSSECTION</c>; null when not set.</summary>
    public string? CrossSection { get; set; }

    /// <summary>The display text of <c>CONNECTION_WIRENUMBER</c>; null when not set.</summary>
    public string? Colour { get; set; }
}
