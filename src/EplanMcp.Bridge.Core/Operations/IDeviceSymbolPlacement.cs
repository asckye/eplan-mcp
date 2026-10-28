using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>One resolved symbol on the main thread; Begin uses Transaction, never SafetyPoint (A13/A52/A53).</summary>
public interface IDeviceSymbolPlacement : ITransactionScopeFactory
{
    /// <summary>Creates and verifies the function, displays and indexed pins; releases lists before returning or throwing (A55).</summary>
    public DevicePlaceData Place(DevicePlaceArgs args, string tag);
}
