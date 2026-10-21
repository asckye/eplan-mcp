namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.CableGet"/> (docs/tools/eplan_cable_get.md); the slice is of the cores.</summary>
public sealed class CableGetArgs : ListArgs
{
    /// <summary>The cable's tag as eplan_device_get takes it: the full tag, or a shown tag such as <c>-WG2</c> that one device has.</summary>
    public string? Cable { get; set; }

    /// <summary>Language code for multi-language texts, for example <c>zh_CN</c>; null means EPLAN's user interface language.</summary>
    public string? Language { get; set; }
}
