namespace EplanMcp.Contracts;

/// <summary>Shared source restrictions for the optional secondary-symbol mode.</summary>
public static class DeviceJoinMode
{
    public static string? Problem(DevicePlaceArgs args) => args.JoinDevice.HasValue
        && (args.Variant is not null && args.Part is null || args.Library is null || args.Symbol is null
            || args.LocationBox is not null || args.TerminalStrip is not null || args.TerminalNumber is not null
            || args.PlcConnectionPoint is not null || args.PlcAddress is not null || args.PlcSymbolicAddress is not null
            || args.PlcText is not null || args.PlcPlugDesignation is not null
            || string.Equals(args.Library, "SPECIAL", StringComparison.OrdinalIgnoreCase) && string.Equals(args.Symbol, "PLCC", StringComparison.OrdinalIgnoreCase))
        ? "/join_device: use library/symbol for the second symbol; terminal and PLC routes are not supported."
        : null;
}
