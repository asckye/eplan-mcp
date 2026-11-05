namespace EplanMcp.Contracts;

/// <summary>Part templates are preferred except for joined functions and SPECIAL symbols.</summary>
public static class DevicePlacementRoute
{
    public static bool UsesPart(DevicePlaceArgs args) => args.Part is not null && args.JoinDevice != true
        && !string.Equals(args.Library, "SPECIAL", StringComparison.OrdinalIgnoreCase);
}
