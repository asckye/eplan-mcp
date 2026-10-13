using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Server;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Bridge.Eplan.Hosting;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan;

/// <summary>
/// Composition root of the add-in: wires the EPLAN adapters into Bridge.Core and starts the pipe server.
/// Called from <see cref="EplanAddIn"/> on EPLAN's main thread only; the static state has no other owner.
/// </summary>
internal static class BridgeHost
{
    private static readonly TimeSpan _startTimeout = TimeSpan.FromSeconds(10);

    private static BridgeServer? _server;

    /// <summary>Main thread (OnInitGui). Kept out of line so a missing Bridge assembly fails here, inside the caller's catch.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Start()
    {
        if (_server is not null)
        {
            return;
        }

        var log = new FileBridgeLog(DataLocations.Logs);
        string version = typeof(BridgeHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
        log.Write(BridgeLogLevel.Information, $"EplanMcp add-in {version} starting");

        EplanDispatcher dispatcher = EplanDispatcher.CaptureOnMainThread(out string dispatcherSource);
        log.Write(BridgeLogLevel.Information, "Main-thread dispatcher: " + dispatcherSource);

        EplanInstanceInfo eplan = EplanEnvironment.Capture(log);
        log.Write(BridgeLogLevel.Information, $"EPLAN {eplan.Version}, variant {eplan.Variant ?? "unknown"}, API extension licence {eplan.ApiExtensionLicence?.ToString() ?? "unknown"}");

        var options = new BridgeServerOptions { BridgeVersion = version, Eplan = eplan };
        var router = new OperationRouter([new ProjectListOperation(new EplanProjectCatalog())], dispatcher, new EplanExceptionMapper(), log, _startTimeout);
        var pipes = new SecurePipeFactory(options.MaxConnections);
        _server = new BridgeServer(BridgeServer.CreatePipeName(eplan.ProcessId), pipes.Create, options, router, log);
        _server.Start();
    }

    /// <summary>Main thread (OnExit). Does not wait for the listeners (see <see cref="BridgeServer.Dispose"/>).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Stop()
    {
        _server?.Dispose();
        _server = null;
    }

    /// <summary>
    /// Last-resort report when <see cref="Start"/> or <see cref="Stop"/> failed, possibly because Bridge.Core could not
    /// be loaded: BCL only, never throws.
    /// </summary>
    public static void ReportStartFailure(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(DataLocations.Logs);
            File.AppendAllText(
                Path.Combine(DataLocations.Logs, "bridge-startup-error.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + exception + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Nowhere left to report to; EPLAN keeps running without the Bridge.
        }
    }
}
