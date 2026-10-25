namespace EplanMcp.Contracts;

/// <summary>One verified version/variant/licence combination, never a cross-product of separate allowlists.</summary>
public sealed class PropertyWriteEnvironment
{
    /// <summary>The full EPLAN version recorded by the scratch gates.</summary>
    public string EplanVersion { get; set; } = string.Empty;

    /// <summary>The verified EPLAN variant.</summary>
    public string Variant { get; set; } = string.Empty;

    /// <summary>The verified licence option combination.</summary>
    public List<string> LicenceOptions { get; set; } = [];
}
