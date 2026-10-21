using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A device as <see cref="IPlcReader"/> reads it: EPLAN's values, unchanged.</summary>
public sealed class PlcRecord
{
    /// <summary>The main function's <c>FunctionCategory</c> name, e.g. <c>PLCBox</c>.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>The PLC box, <c>VisibleTag</c> holding <c>VisibleName</c> as EPLAN returns it; null when the device is not one.</summary>
    public PlcInfo? Plc { get; set; }

    /// <summary>Every channel object in EPLAN's order (<c>PLC.PLCTerminals</c>), none left out by name; <c>Connected</c> is empty.</summary>
    public List<PlcChannel> Channels { get; set; } = [];

    /// <summary>One entry per end of a connection that is at the PLC box, plc.get gives it to its channel.</summary>
    public List<PlcConnectionRecord> Connections { get; set; } = [];
}

/// <summary>The end of a connection at the PLC box, as <see cref="IPlcReader"/> reads it, and the connection's other end.</summary>
public sealed class PlcConnectionRecord
{
    /// <summary>The index in <see cref="PlcRecord.Channels"/> of the channel object EPLAN gives as this end's function; null when it is none of them.</summary>
    public int? Channel { get; set; }

    /// <summary>The name of this end's function (<c>Pin.ParentFunction.Name</c>), e.g. <c>=K1+A2-KE1:2.3</c>.</summary>
    public string? Function { get; set; }

    /// <summary>Full name of the connection's page; null when EPLAN gives none.</summary>
    public string? Page { get; set; }

    /// <summary>The other end, empty names included; both null when that side is not connected.</summary>
    public ConnectionEnd Other { get; set; } = new();
}
