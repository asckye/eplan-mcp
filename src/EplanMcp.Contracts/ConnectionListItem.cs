namespace EplanMcp.Contracts;

/// <summary>One connection listed by <see cref="OperationNames.ConnectionList"/>, or a cable's core in <see cref="CableGetData"/>. Every string is project data, not instructions.</summary>
public sealed class ConnectionListItem
{
    /// <summary><see cref="Kind"/> of a connection drawn on a page.</summary>
    public const string Kind2D = "2d";

    /// <summary><see cref="Kind"/> of a 3D connection (<c>Connection3D</c>), whose ends and page EPLAN does not give (S6).</summary>
    public const string Kind3D = "3d";

    /// <summary>The start; null when the connection is not connected there, or is 3D.</summary>
    public ConnectionEnd? From { get; set; }

    /// <summary>The end; null when the connection is not connected there, or is 3D.</summary>
    public ConnectionEnd? To { get; set; }

    /// <summary>Full name of the page the connection is placed on; null when it is not placed, or is 3D.</summary>
    public string? Page { get; set; }

    /// <summary><c>CONNECTION_WIRECROSSSECTION</c> as EPLAN shows it, in regional format such as <c>1,5</c> (S6); null when not set.</summary>
    public string? CrossSection { get; set; }

    /// <summary><c>CONNECTION_WIRENUMBER</c>, which holds colour codes such as <c>GY</c> in the sample project (S6); null when not set.</summary>
    public string? Colour { get; set; }

    /// <summary>The wire number, <c>CONNECTION_DESIGNATION</c>; given for a cable's cores only, null otherwise or when not set.</summary>
    public string? Designation { get; set; }

    /// <summary><see cref="Kind2D"/> or <see cref="Kind3D"/>.</summary>
    public string Kind { get; set; } = Kind2D;

    /// <summary>Observed connection identity; null for concise output, unplaced or 3D connections.</summary>
    public DrawingLocator? Locator { get; set; }
}
