using System.Diagnostics;
using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Bridge;

/// <summary>
/// Lists <c>\\.\pipe\</c> and keeps pipes named <c>&lt;prefix&gt;&lt;pid&gt;.&lt;random&gt;</c> whose process is alive
/// in this Windows session (ADR-0008). The session filter keeps other users' EPLAN instances on a terminal server
/// out of the list. The prefix can be overridden with <c>EPLANMCP_PIPE_PREFIX</c>, for tests only.
/// </summary>
internal sealed class PipeBridgeDiscovery : IBridgeDiscovery
{
    public const string PrefixVariable = "EPLANMCP_PIPE_PREFIX";

    private static readonly int _session = CurrentSession();

    private readonly string _prefix;

    public PipeBridgeDiscovery(string? prefix = null)
    {
        _prefix = prefix
            ?? (Environment.GetEnvironmentVariable(PrefixVariable) is { Length: > 0 } fromEnvironment ? fromEnvironment : BridgeProtocol.PipePrefix);
    }

    public IReadOnlyList<BridgeEndpoint> Discover()
    {
        var found = new List<BridgeEndpoint>();
        foreach (string path in Directory.EnumerateFiles(@"\\.\pipe\"))
        {
            if (Parse(Path.GetFileName(path), _prefix) is { } endpoint && IsInOurSession(endpoint.ProcessId))
            {
                found.Add(endpoint);
            }
        }

        return found;
    }

    /// <summary>Parses <c>&lt;prefix&gt;&lt;pid&gt;.&lt;random&gt;</c>; null for any other name.</summary>
    internal static BridgeEndpoint? Parse(string pipeName, string prefix)
    {
        if (!pipeName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        string rest = pipeName[prefix.Length..];
        int dot = rest.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 && dot < rest.Length - 1
            && int.TryParse(rest.AsSpan(0, dot), NumberStyles.None, CultureInfo.InvariantCulture, out int pid)
            ? new BridgeEndpoint(pipeName, pid)
            : null;
    }

    /// <summary>
    /// GetProcessById succeeds only for a running process, and SessionId comes from the process snapshot, so no
    /// process handle is needed: an elevated EPLAN or another user's EPLAN would deny one (HasExited would throw).
    /// </summary>
    private static bool IsInOurSession(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.SessionId == _session;
        }
        catch (ArgumentException)
        {
            return false; // the process is gone; its pipe is about to disappear
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static int CurrentSession()
    {
        using var process = Process.GetCurrentProcess();
        return process.SessionId;
    }
}
