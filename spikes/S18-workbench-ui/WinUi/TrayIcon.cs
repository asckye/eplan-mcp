using System.Globalization;
using System.Runtime.InteropServices;

namespace EplanMcp.Spikes.S18.WinUi;

/// <summary>
/// Minimal Win32 tray icon (ADR-0015 item 3): a hidden top-level window on the UI thread receives the
/// Shell_NotifyIcon callback, so WinUI's message loop dispatches it and the click handler runs on the UI thread.
/// Top-level rather than message-only, so that it also receives the TaskbarCreated broadcast after Explorer restarts.
/// </summary>
internal sealed unsafe partial class TrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8000 + 1; // WM_APP + 1
    private const uint NinSelect = 0x0400;
    private const uint NinKeySelect = 0x0401;
    private const uint IconId = 1;

    private readonly WndProc _wndProc;
    private readonly IntPtr _window;
    private readonly uint _taskbarCreated;
    private readonly Action<string> _onClick;
    private string _tip;
    private bool _disposed;

    internal TrayIcon(string tip, Action<string> onClick)
    {
        _tip = tip;
        _onClick = onClick;
        _wndProc = WindowProc;
        IntPtr instance = NativeMethods.GetModuleHandleW(IntPtr.Zero);
        fixed (char* className = "EplanMcpS18TrayWindow")
        {
            var windowClass = new WndClassEx
            {
                Size = (uint)sizeof(WndClassEx),
                WndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                Instance = instance,
                ClassName = (IntPtr)className,
            };
            ushort atom = NativeMethods.RegisterClassExW(&windowClass);
            _window = NativeMethods.CreateWindowExW(0, atom, IntPtr.Zero, 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        }

        _taskbarCreated = NativeMethods.RegisterWindowMessageW("TaskbarCreated");
        Add();
    }

    private delegate IntPtr WndProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    internal IntPtr WindowHandle => _window;

    internal void SetTip(string tip)
    {
        _tip = tip;
        NotifyIconData data = Data();
        NativeMethods.Shell_NotifyIconW(1 /* NIM_MODIFY */, &data);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        NotifyIconData data = Data();
        NativeMethods.Shell_NotifyIconW(2 /* NIM_DELETE */, &data);
        NativeMethods.DestroyWindow(_window);
    }

    private void Add()
    {
        NotifyIconData data = Data();
        bool added = NativeMethods.Shell_NotifyIconW(0 /* NIM_ADD */, &data);
        data.TimeoutOrVersion = 4; // NOTIFYICON_VERSION_4: lParam carries NIN_SELECT, NIN_KEYSELECT
        bool versioned = NativeMethods.Shell_NotifyIconW(4 /* NIM_SETVERSION */, &data);
        var id = new NotifyIconIdentifier { Size = (uint)sizeof(NotifyIconIdentifier), Window = _window, Id = IconId };
        int hr = NativeMethods.Shell_NotifyIconGetRect(&id, out Rect rect);
        SpikeLog.Write("tray-added", string.Create(CultureInfo.InvariantCulture,
            $"add={added} version={versioned} rectHr=0x{hr:X8} rect={rect.Left},{rect.Top},{rect.Right},{rect.Bottom}"));
    }

    private NotifyIconData Data()
    {
        var data = new NotifyIconData
        {
            Size = (uint)sizeof(NotifyIconData),
            Window = _window,
            Id = IconId,
            Flags = 0x01 | 0x02 | 0x04 | 0x80, // NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP
            CallbackMessage = CallbackMessage,
            Icon = NativeMethods.LoadIconW(IntPtr.Zero, 32512 /* IDI_APPLICATION */),
        };
        ReadOnlySpan<char> tip = _tip.AsSpan(0, Math.Min(_tip.Length, 127));
        tip.CopyTo(new Span<char>(data.Tip, 128));
        return data;
    }

    private IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == CallbackMessage)
        {
            uint code = (uint)(lParam.ToInt64() & 0xFFFF);
            if (code is NinSelect or NinKeySelect)
            {
                _onClick(code == NinSelect ? "tray-click" : "tray-key");
            }

            return IntPtr.Zero;
        }

        if (message == _taskbarCreated && _taskbarCreated != 0)
        {
            Add();
        }

        return NativeMethods.DefWindowProcW(window, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public IntPtr WndProc;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public IntPtr MenuName;
        public IntPtr ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;
        public fixed char Tip[128];
        public uint State;
        public uint StateMask;
        public fixed char Info[256];
        public uint TimeoutOrVersion;
        public fixed char InfoTitle[64];
        public uint InfoFlags;
        public Guid GuidItem;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public Guid GuidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("kernel32.dll")]
        internal static partial IntPtr GetModuleHandleW(IntPtr name);

        [LibraryImport("user32.dll")]
        internal static partial ushort RegisterClassExW(WndClassEx* windowClass);

        [LibraryImport("user32.dll")]
        internal static partial IntPtr CreateWindowExW(uint exStyle, nint classAtom, IntPtr windowName, uint style, int x, int y,
            int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DestroyWindow(IntPtr window);

        [LibraryImport("user32.dll")]
        internal static partial IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial uint RegisterWindowMessageW(string name);

        [LibraryImport("user32.dll")]
        internal static partial IntPtr LoadIconW(IntPtr instance, nint name);

        [LibraryImport("shell32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool Shell_NotifyIconW(uint message, NotifyIconData* data);

        [LibraryImport("shell32.dll")]
        internal static partial int Shell_NotifyIconGetRect(NotifyIconIdentifier* identifier, out Rect rect);
    }
}
