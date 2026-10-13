namespace EplanMcp.Bridge.Core.Diagnostics;

/// <summary>
/// Minimal logging seam for the Bridge. Microsoft.Extensions.Logging is not in EPLAN's Bin directory, and the
/// Bridge carries no third-party dependencies (ADR-0003), so the add-in supplies a small file logger.
/// Implementations must never throw.
/// </summary>
public interface IBridgeLog
{
    public void Write(BridgeLogLevel level, string message, Exception? exception = null);
}
