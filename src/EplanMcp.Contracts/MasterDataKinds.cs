namespace EplanMcp.Contracts;

/// <summary>The system master data a write can change (ADR-0019); the Host and the Bridge use these names only.</summary>
public static class MasterDataKinds
{
    /// <summary>The parts database EPLAN is configured with (<c>MDTYPE:ARTICLES</c>, archive <c>.zw6</c>).</summary>
    public const string Parts = "parts";
}
