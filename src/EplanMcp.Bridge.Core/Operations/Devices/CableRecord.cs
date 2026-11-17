using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A device as <see cref="ICableReader"/> reads it: EPLAN's values, unchanged.</summary>
public sealed class CableRecord
{
    /// <summary>The main function's <c>FunctionCategory</c> name, e.g. <c>Cable</c>.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>The cable, <c>VisibleTag</c> holding <c>VisibleName</c> as EPLAN returns it; null when the device is not a cable.</summary>
    public CableInfo? Cable { get; set; }

    /// <summary>Every core (<c>Cable.CableConnections</c>) in EPLAN's order, with its designation.</summary>
    public List<ConnectionRecord> Cores { get; set; } = [];
}
