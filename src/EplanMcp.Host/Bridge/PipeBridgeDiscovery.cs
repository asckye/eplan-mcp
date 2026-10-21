using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Bridge;

/// <summary>
/// Lists the pipes in <c>\\.\pipe\</c> that start with the prefix and keeps those named
/// <c>&lt;prefix&gt;&lt;pid&gt;.&lt;random&gt;</c> whose process is alive in this Windows session (ADR-0008). The
/// session filter keeps other users' EPLAN instances on a terminal server out of the list. The prefix can be
/// overridden with <c>EPLANMCP_PIPE_PREFIX</c>, for tests only.
/// </summary>
internal sealed class PipeBridgeDiscovery : IBridgeDiscovery
{
    public const string PrefixVariable = "EPLANMCP_PIPE_PREFIX";

    private const int FindExInfoBasic = 1;
    private const int FindExSearchNameMatch = 0;
    private const int FindFirstExLargeFetch = 2;
    private const int ErrorFileNotFound = 2;
    private const int ErrorNoMoreFiles = 18;

    /// <summary>
    /// A constant, not a static field: the first static field read would run this type's initializer, whose Process
    /// calls overwrite the error of the call just made.
    /// </summary>
    private const nint InvalidHandle = -1;

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
        foreach (string name in ListPipes(_prefix))
        {
            if (Parse(name, _prefix) is { } endpoint && IsInOurSession(endpoint.ProcessId))
            {
                found.Add(endpoint);
            }
        }

        return found;
    }

    /// <summary>
    /// The pipes whose names start with <paramref name="prefix"/>, with the prefix as the query mask: the pipe file
    /// system filters, so the few matches arrive in the first query. Directory.EnumerateFiles reads the whole namespace
    /// (hundreds of pipes) in several queries, each continuing at a position, and every pipe that closes in between
    /// moves a newer one into the part already read, which is then skipped; a Bridge started moments ago is the newest,
    /// so on a busy machine it was the one missed. The mask's wildcards match more than the prefix, never less, and
    /// <see cref="Parse"/> is exact.
    /// </summary>
    private static List<string> ListPipes(string prefix)
    {
        var names = new List<string>();
        IntPtr find = FindFirstFileEx(@"\\.\pipe\" + prefix + "*", FindExInfoBasic, out FindData data, FindExSearchNameMatch, IntPtr.Zero, FindFirstExLargeFetch);
        if (find == InvalidHandle)
        {
            int error = Marshal.GetLastPInvokeError();
            return error is ErrorFileNotFound or ErrorNoMoreFiles ? names : throw new Win32Exception(error);
        }

        try
        {
            do
            {
                names.Add(data.FileName);
            }
            while (FindNextFile(find, out data));

            int error = Marshal.GetLastPInvokeError();
            return error == ErrorNoMoreFiles ? names : throw new Win32Exception(error);
        }
        finally
        {
            FindClose(find);
        }
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

    [DllImport("kernel32.dll", EntryPoint = "FindFirstFileExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindFirstFileEx(string fileName, int infoLevel, out FindData findData, int searchOp, IntPtr searchFilter, int additionalFlags);

    [DllImport("kernel32.dll", EntryPoint = "FindNextFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextFile(IntPtr find, out FindData findData);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(IntPtr find);

    /// <summary>WIN32_FIND_DATAW.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FindData
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint Reserved0;
        public uint Reserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string FileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string AlternateFileName;
    }
}
