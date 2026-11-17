namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.PlcGet"/> (docs/tools/eplan_plc_get.md); the slice is of the channels.</summary>
public sealed class PlcGetArgs : ListArgs
{
    /// <summary>The PLC box's tag as eplan_device_get takes it: the full tag, or a shown tag such as <c>-KE1</c> that one device has.</summary>
    public string? Plc { get; set; }

    /// <summary>Language code for multi-language texts, for example <c>zh_CN</c>; null means EPLAN's user interface language.</summary>
    public string? Language { get; set; }

    /// <summary>Include native owner, manual symbolic address, card reference and parentless representation count.</summary>
    public bool Detailed { get; set; }
}
