using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace EplanMcp.Workbench.Approvals;

/// <summary>
/// Who is at the other end of a server pipe (ADR-0014 decision 2): its process and whether that process runs the
/// expected image. A guard against a mismatched build or another program, not authentication: a program of the same
/// user is outside what this protects (security.md section 2).
/// </summary>
internal static class PipeClient
{
    private const uint QueryLimitedInformation = 0x1000;

    /// <summary>The client's process ID; 0 when it cannot be read.</summary>
    public static int ProcessId(NamedPipeServerStream pipe) =>
        GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint processId) ? (int)processId : 0;

    /// <summary>Whether <paramref name="processId"/> runs the file at <paramref name="expected"/>; false when either cannot be read.</summary>
    public static bool Runs(int processId, string expected)
    {
        using SafeProcessHandle process = OpenProcess(QueryLimitedInformation, inheritHandle: false, (uint)processId);
        char[] image = new char[32_768];
        int length = image.Length;
        return !process.IsInvalid && QueryFullProcessImageName(process, 0, image, ref length) && SameFile(new string(image, 0, length), expected);
    }

    /// <summary>
    /// Whether two paths lead to one file: same volume and file index, so that links, short names and letter case do not
    /// matter. False when either cannot be opened.
    /// </summary>
    public static bool SameFile(string first, string second)
    {
        try
        {
            using SafeFileHandle a = File.OpenHandle(first, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using SafeFileHandle b = File.OpenHandle(second, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return GetFileInformationByHandle(a, out FileInformation one) && GetFileInformationByHandle(b, out FileInformation two)
                && one.VolumeSerialNumber == two.VolumeSerialNumber && one.FileIndexHigh == two.FileIndexHigh && one.FileIndexLow == two.FileIndexLow;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, [Out] char[] image, ref int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);

    /// <summary>BY_HANDLE_FILE_INFORMATION.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
