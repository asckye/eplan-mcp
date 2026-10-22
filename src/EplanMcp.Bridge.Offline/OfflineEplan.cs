using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.System;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Hosting;
using EplanMcp.Bridge.Core.Server;
using EplanMcp.Bridge.Eplan;
using EplanMcp.Bridge.Eplan.Adapters;

namespace EplanMcp.Bridge.Offline;

/// <summary>
/// EPLAN without a user interface, on this process's STA main thread (api-notes B5, A22): Init, the project, the
/// add-in's Bridge, the WPF dispatcher until the idle exit or Ctrl+C, then Exit. Only called once the assembly
/// resolver points at EPLAN. A failure is logged in full here and goes on to the caller, which reports it on stderr.
/// Nothing here depends on the current culture, which Init changes to EPLAN's (A22).
/// </summary>
internal static class OfflineEplan
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run(OfflineHostOptions options, string platformBin, IBridgeLog log)
    {
        log.Write(BridgeLogLevel.Information, $"EplanMcp offline host {BridgeHost.Version} starting");
        var application = new EplApplication { EplanBinFolder = options.VariantBin };
        try
        {
            // No licence or login dialog: nobody could answer it (A22).
            application.Init(string.Empty, false, false);
        }
        catch (Exception ex)
        {
            log.Write(BridgeLogLevel.Error, "EPLAN did not start (EplApplication.Init)", ex);
            throw;
        }

        try
        {
            Serve(options, platformBin, log);
            return 0;
        }
        catch (Exception ex)
        {
            log.Write(BridgeLogLevel.Error, "The offline host failed", ex);
            throw;
        }
        finally
        {
            // Once, on the main thread, after a successful Init (B5).
            application.Exit();
            log.Write(BridgeLogLevel.Information, "EPLAN exited");
        }
    }

    private static void Serve(OfflineHostOptions options, string platformBin, IBridgeLog log)
    {
        Project project = Open(options.Project, options.ReadOnly);
        log.Write(BridgeLogLevel.Information, options.ReadOnly ? "Opened the project read-only" : "Opened the project");
        try
        {
            using BridgeServer server = BridgeHost.Start(log, options.PipePrefix, l => EplanEnvironment.CaptureOffline(l, options.VariantBin, platformBin));
            RunDispatcher(server, options.IdleExit, log);
        }
        finally
        {
            Close(project, log);
        }
    }

    /// <summary>Outside an action every EPLAN call needs a LockingStep (A9, B7); spike S15 opened its copy so.</summary>
    /// <remarks>
    /// A completed revision (.elr) is write-protected (S23 r3), so it is opened with <c>OpenMode.ReadOnly</c> (the enum's
    /// members are known by reflection only); whether OpenProject takes .ell and .elr at all is left to the Large tests.
    /// </remarks>
    private static Project Open(string path, bool readOnly)
    {
        using (new LockingStep())
        {
            using var manager = new ProjectManager();
            return readOnly ? manager.OpenProject(path, ProjectManager.OpenMode.ReadOnly) : manager.OpenProject(path);
        }
    }

    private static void Close(Project project, IBridgeLog log)
    {
        try
        {
            using (new LockingStep())
            {
                project.Close();
            }

            log.Write(BridgeLogLevel.Information, "Closed the project");
        }
        catch (Exception ex)
        {
            log.Write(BridgeLogLevel.Warning, "Could not close the project; EPLAN exits anyway", ex);
        }
    }

    /// <summary>
    /// Runs the main thread's WPF dispatcher, where the Bridge's operations run as in the add-in. Once no Host has been
    /// connected for <paramref name="idleExit"/>, or after Ctrl+C, the pipe stops; the dispatcher runs on until the
    /// connections still open have ended, so that an operation already sent finishes before the project is closed.
    /// </summary>
    private static void RunDispatcher(BridgeServer server, TimeSpan? idleExit, IBridgeLog log)
    {
        var frame = new DispatcherFrame();
        int interrupted = 0; // set on the Ctrl+C handler's thread
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            if (e.SpecialKey == ConsoleSpecialKey.ControlC)
            {
                e.Cancel = true; // stop through Exit instead of ending the process
                Interlocked.Exchange(ref interrupted, 1);
            }
        };
        bool stopping = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            bool idle = idleExit is { } limit && DateTime.UtcNow - server.IdleSinceUtc >= limit;
            if (!stopping && (idle || Volatile.Read(ref interrupted) == 1))
            {
                stopping = true;
                string seconds = idleExit?.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) ?? string.Empty;
                log.Write(BridgeLogLevel.Information, idle ? $"No Host connected for {seconds} s: stopping" : "Ctrl+C: stopping");
                server.Dispose();
            }

            if (stopping && server.IdleSinceUtc is not null)
            {
                frame.Continue = false;
            }
        };

        Console.CancelKeyPress += onCancel;
        timer.Start();
        try
        {
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            timer.Stop();
            Console.CancelKeyPress -= onCancel;
        }
    }
}
