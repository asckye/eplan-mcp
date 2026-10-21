using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace EplanMcp.Spikes.S18.WinUi;

/// <summary>
/// Own Main (DISABLE_XAML_GENERATED_MAIN) for single instance: the first process registers the key, a second one
/// redirects its activation to the first and exits before any XAML starts. Follows Microsoft's "single-instanced
/// WinUI app" sample: the redirect runs on a worker thread while the STA main thread waits with
/// CoWaitForMultipleObjects.
/// </summary>
internal static partial class Program
{
    private const string InstanceKey = "EplanMcp.Spikes.S18.WinUi";

    internal static SpikeOptions Options { get; private set; } = SpikeOptions.Parse([]);

    [STAThread]
    private static int Main(string[] args)
    {
        Options = SpikeOptions.Parse(args);
        SpikeLog.Init(Options.LogPath);
        SpikeLog.Write("main");
        WinRT.ComWrappersSupport.InitializeComWrappers();

        AppInstance key = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!key.IsCurrent)
        {
            Redirect(key);
            SpikeLog.Write("redirected", key.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return 0;
        }

        key.Activated += (_, _) =>
        {
            SpikeLog.Write("activated-by-second-instance");
            App.Instance?.OnRedirected();
        };

        Application.Start(callback =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        SpikeLog.Write("exit");
        return 0;
    }

    private static void Redirect(AppInstance key)
    {
        AppActivationArguments activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        IntPtr done = NativeMethods.CreateEventW(IntPtr.Zero, true, false, null);
        _ = Task.Run(() =>
        {
            key.RedirectActivationToAsync(activation).AsTask().Wait();
            NativeMethods.SetEvent(done);
        });
        _ = NativeMethods.CoWaitForMultipleObjects(0, 0xFFFFFFFF, 1, [done], out _);
        NativeMethods.CloseHandle(done);

        // The second instance was started by the user, so it may hand the foreground to the first one.
        using var first = Process.GetProcessById((int)key.ProcessId);
        NativeMethods.SetForegroundWindow(first.MainWindowHandle);
    }

    private static partial class NativeMethods
    {
        [LibraryImport("kernel32.dll", EntryPoint = "CreateEventW", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial IntPtr CreateEventW(IntPtr attributes, [MarshalAs(UnmanagedType.Bool)] bool manualReset,
            [MarshalAs(UnmanagedType.Bool)] bool initialState, string? name);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetEvent(IntPtr handle);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CloseHandle(IntPtr handle);

        [LibraryImport("ole32.dll")]
        internal static partial int CoWaitForMultipleObjects(uint flags, uint timeout, uint count, IntPtr[] handles, out uint index);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetForegroundWindow(IntPtr window);
    }
}
