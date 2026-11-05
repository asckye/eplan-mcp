using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Reacquires PLC objects by identifier after a confirmed commit; no transaction-bound native handles are retained (A55).</summary>
public interface IDeviceSymbolCommittedReadback
{
    /// <summary>Refreshes native PLC fields and verifies displays; failure becomes a committed warning, never an unknown commit.</summary>
    public void ReadCommitted(DevicePlaceArgs args, DevicePlaceData data);

    /// <summary>Adds the requested part after the placement's commit, inside its undo step; a part EPLAN did not take rolls the placement back (A95).</summary>
    public void AttachPart(DevicePlaceArgs args, DevicePlaceData data);

    /// <summary>Assigns a matching unplaced function after creation commits, within the same undo step.</summary>
    public void AssignUnplacedFunction(DevicePlaceArgs args, DevicePlaceData data);
}
