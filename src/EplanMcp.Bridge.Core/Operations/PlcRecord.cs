using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A device as <see cref="IPlcReader"/> reads it: EPLAN's values, unchanged.</summary>
public sealed class PlcRecord
{
    /// <summary>The main function's <c>FunctionCategory</c> name, e.g. <c>PLCBox</c>.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>The PLC box, <c>VisibleTag</c> holding <c>VisibleName</c> as EPLAN returns it; null when the device is not one.</summary>
    public PlcInfo? Plc { get; set; }

    /// <summary>Every channel in EPLAN's order, with every connection end EPLAN gives, empty names included.</summary>
    public List<PlcChannel> Channels { get; set; } = [];
}
