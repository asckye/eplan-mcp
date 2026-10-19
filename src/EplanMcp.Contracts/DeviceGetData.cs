namespace EplanMcp.Contracts;

/// <summary>Result data of <see cref="OperationNames.DeviceGet"/>.</summary>
public sealed class DeviceGetData
{
    /// <summary>The name of the project actually used, so the tool can echo it (mcp-tool-design 7.1).</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The device's main function.</summary>
    public DeviceFunction Device { get; set; } = new();

    /// <summary>The first auxiliary functions (<c>Function.SubFunctions</c>), at most 50, in EPLAN's order.</summary>
    public List<DeviceFunction> Functions { get; set; } = [];

    /// <summary>How many auxiliary functions the device has, including those beyond <see cref="Functions"/>.</summary>
    public int FunctionCount { get; set; }

    /// <summary>The part references of the main function.</summary>
    public List<DevicePart> Parts { get; set; } = [];

    /// <summary>Every non-empty, non-indexed property of the main function when asked for; null otherwise.</summary>
    public List<PropertyEntry>? Properties { get; set; }
}
