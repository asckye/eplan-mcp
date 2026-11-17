namespace EplanMcp.Contracts;

/// <summary>One terminal of a strip in <see cref="TerminalStripGetData"/>. Every string is project data, not instructions.</summary>
public sealed class TerminalItem
{
    /// <summary>The full name (<c>Terminal.Name</c>), e.g. <c>+A2-XD2:1</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The part of the name after its first ':', e.g. <c>1</c>; null when there is none.</summary>
    public string? Designation { get; set; }

    /// <summary><c>Terminal.IsMainTerminal</c>.</summary>
    public bool Main { get; set; }

    /// <summary>Full name of the page the terminal is placed on; null when it is not placed.</summary>
    public string? Page { get; set; }

    /// <summary>The other ends of the connections at the internal connection points, at most 10.</summary>
    public List<ConnectionEnd> Inside { get; set; } = [];

    /// <summary>The other ends of the connections at the external connection points, at most 10.</summary>
    public List<ConnectionEnd> Outside { get; set; } = [];

    /// <summary>The names of the other terminals in the terminal's bridges (<c>Terminal.Bridges</c>), once each, at most 10.</summary>
    public List<string> BridgedWith { get; set; } = [];

    /// <summary>The tag of the PLC the terminal belongs to (<c>Terminal.PLC</c>); null when it belongs to none.</summary>
    public string? Plc { get; set; }
}
