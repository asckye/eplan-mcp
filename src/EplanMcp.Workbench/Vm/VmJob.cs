using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace EplanMcp.Workbench.Vm;

internal sealed class VmJob : IDisposable
{
    private readonly SafeFileHandle _handle = CreateJobObject(IntPtr.Zero, IntPtr.Zero);
    public VmJob()
    {
        var limits = new Limits { Flags = 0x2000 }; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE; handle is not inheritable.
        if (_handle.IsInvalid || !SetInformationJobObject(_handle, 9, in limits, Marshal.SizeOf<Limits>()))
        {
            int error = Marshal.GetLastWin32Error();
            _handle.Dispose();
            throw new Win32Exception(error);
        }
    }

    public void Assign(IntPtr process)
    {
        if (!AssignProcessToJobObject(_handle, process))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void Dispose() => _handle.Dispose();
    // JOBOBJECT_EXTENDED_LIMIT_INFORMATION, x64 (the Workbench's required platform).
    [StructLayout(LayoutKind.Explicit, Size = 144)]
    private struct Limits { [FieldOffset(16)] public uint Flags; }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, IntPtr name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, in Limits limits, int length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}
