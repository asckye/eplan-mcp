using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Reacquires PLC objects by identifier after a confirmed commit; no transaction-bound native handles are retained (A55).</summary>
public interface IDeviceSymbolCommittedReadback
{
    /// <summary>Refreshes native PLC fields and verifies displays; failure becomes a committed warning, never an unknown commit.</summary>
    public void ReadCommitted(DevicePlaceArgs args, DevicePlaceData data);
}
