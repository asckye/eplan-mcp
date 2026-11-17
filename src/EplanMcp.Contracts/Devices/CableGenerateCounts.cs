namespace EplanMcp.Contracts;

/// <summary>Observed cable functions and connections, as counted in S34 (api-notes A39).</summary>
public sealed class CableGenerateCounts
{
    /// <summary>All functions of category Cable.</summary>
    public int Cables { get; set; }

    /// <summary>Cable functions whose automatic flag is explicitly true.</summary>
    public int Automatic { get; set; }

    /// <summary>Cable functions whose automatic flag is unset.</summary>
    public int AutomaticUnknown { get; set; }

    /// <summary>All project connections.</summary>
    public int Connections { get; set; }
}
