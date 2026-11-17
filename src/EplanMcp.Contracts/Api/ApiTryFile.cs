namespace EplanMcp.Contracts;
/// <summary>A trial artifact below exports; never contains a local absolute path.</summary>
public sealed class ApiTryFile
{
    /// <summary>Path relative to the Host exports folder.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>File length in bytes.</summary>
    public long Bytes { get; set; }
    /// <summary>Lowercase SHA-256 of the file contents.</summary>
    public string Sha256 { get; set; } = string.Empty;
}
