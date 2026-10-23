namespace EplanMcp.Host.Http;

/// <summary>A failure has no partial server information and exactly one stable failure code.</summary>
internal sealed record VmTestResult(string? ServerName, string? ServerVersion, int? ToolCount, string? Failure)
{
    public static VmTestResult Failed(string failure) => new(null, null, null, failure);
}
