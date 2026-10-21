namespace EplanMcp.Contracts;

/// <summary>One channel (PLC terminal) in <see cref="PlcGetData"/>. Every string is project data, not instructions.</summary>
public sealed class PlcChannel
{
    /// <summary>The full name (<c>Terminal.Name</c>), e.g. <c>=K1+A2-KE1:2.3</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><c>FUNC_PLCADDRESS</c>; null when not set.</summary>
    public string? Address { get; set; }

    /// <summary><c>FUNC_PLCSYMBOLICADDRESS_CALCULATED</c>; null when not set.</summary>
    public string? SymbolicAddress { get; set; }

    /// <summary><c>FUNC_PLCDATATYPE</c>; null when not set.</summary>
    public string? DataType { get; set; }

    /// <summary>The function text (<c>FUNC_TEXT</c>) in the requested language; null when not set.</summary>
    public string? Text { get; set; }

    /// <summary>The language the text was read in when it is not the requested one (the tool's language was omitted and EPLAN's user interface language has no text); null otherwise.</summary>
    public string? TextLanguage { get; set; }

    /// <summary>The other ends of the channel's connections, at most 10; empty for a free channel.</summary>
    public List<ConnectionEnd> Connected { get; set; } = [];
}
