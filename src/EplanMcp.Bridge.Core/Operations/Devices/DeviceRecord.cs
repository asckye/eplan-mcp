using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>One device as <see cref="IDeviceReader.ReadDevice"/> reads it. Part and property texts need no further processing.</summary>
public sealed class DeviceRecord
{
    /// <summary>The main function, with its function text.</summary>
    public FunctionDetail Main { get; set; } = new();

    /// <summary>Every auxiliary function (<c>Function.SubFunctions</c>) in EPLAN's order.</summary>
    public List<FunctionDetail> Auxiliary { get; set; } = [];

    /// <summary>The part references of the main function, described from the project's part data.</summary>
    public List<DevicePart> Parts { get; set; } = [];

    /// <summary>The main function's non-empty properties when they were asked for; null otherwise.</summary>
    public List<PropertyEntry>? Properties { get; set; }
}
