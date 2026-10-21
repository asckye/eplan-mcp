using System.Text.RegularExpressions;
using EplanMcp.Contracts;
using EplanMcp.Host.Addin;

namespace EplanMcp.Host.Tests.Addin;

/// <summary>
/// EPLAN as install-addin sees it, after the live check (api-notes A23): processes it starts, their Bridge pipes and
/// handshakes, and the registration they share. Nothing is ever started for real.
/// <list type="bullet">
/// <item>A registered add-in loads at start-up, from where <see cref="Registered"/> says for the variant.</item>
/// <item><c>/register</c> while nothing is registered registers and loads that DLL; while one is registered and has
/// loaded, it takes only when <see cref="RegisterTakesWhileLoaded"/> (the live check found it does not).</item>
/// <item><c>/unregister</c> clears the registration (unless <see cref="UnregisterTakes"/> is false); the add-in that
/// loaded at start-up stays loaded.</item>
/// </list>
/// A pipe appears after <see cref="PipeAfterLooks"/> looks (never when null). Like a real <c>Process</c>, an instance keeps
/// the window handle it first found, the splash screen: a close request reaches EPLAN only after a refresh (A23).
/// </summary>
internal sealed partial class FakeEplan : IEplanProcesses, IBridgeProbe
{
    private readonly List<Instance> _instances = [];
    private int _nextId = 4000;

    /// <summary>Where EPLAN's settings register the add-in, by variant.</summary>
    public Dictionary<string, string> Registered { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool RegisterTakesWhileLoaded { get; set; }

    public bool UnregisterTakes { get; set; } = true;

    /// <summary>False: an add-in built before the handshake told its path.</summary>
    public bool ReportsPath { get; set; } = true;

    /// <summary>EPLAN processes that ran before the command.</summary>
    public List<int> Others { get; } = [];

    public int? PipeAfterLooks { get; set; } = 2;

    /// <summary>The started EPLAN exits by itself, as one whose sign-in was cancelled.</summary>
    public bool ExitsBeforePipe { get; set; }

    /// <summary>From this start on (0 for the first), the started EPLAN exits by itself.</summary>
    public int? ExitsFromStart { get; set; }

    /// <summary>False: no enabled main window (a modal dialog is open), so CloseMainWindow fails.</summary>
    public bool HasWindow { get; set; } = true;

    /// <summary>How many close requests that reach it an EPLAN does not react to, to show that they are sent again.</summary>
    public int IgnoresCloses { get; set; }

    /// <summary>False: EPLAN takes the close request but stays open (behind a system messages window, for example).</summary>
    public bool ClosesWhenAsked { get; set; } = true;

    public Exception? StartFails { get; set; }

    /// <summary>How many handshakes fail before the Bridge answers (int.MaxValue: never).</summary>
    public int HandshakeFailures { get; set; }

    /// <summary>What happened, in order: "start &lt;exe&gt; &lt;arguments&gt;", "close &lt;id&gt;" for each request.</summary>
    public List<string> Log { get; } = [];

    public IReadOnlyList<Instance> Instances => _instances;

    public IReadOnlyList<int> Running() => [.. Others, .. _instances.Where(i => !i.Exited).Select(i => i.Id)];

    public IStartedEplan Start(string exe, string arguments)
    {
        if (StartFails is { } failure)
        {
            throw failure;
        }

        Log.Add($"start {exe} {arguments}");
        Match action = Action().Match(arguments);
        string variant = action.Groups["variant"].Value;
        string? loaded = Registered.GetValueOrDefault(variant);
        if (action.Groups["register"].Success)
        {
            if (loaded is null || RegisterTakesWhileLoaded)
            {
                Registered[variant] = action.Groups["register"].Value;
            }

            loaded ??= Registered[variant];
        }
        else if (action.Success && UnregisterTakes)
        {
            Registered.Remove(variant);
        }

        var instance = new Instance(this, _nextId++, loaded, _instances.Count);
        _instances.Add(instance);
        return instance;
    }

    public bool IsUp(int processId) => Up(processId) is not null;

    public Task<HandshakeResult?> HandshakeAsync(int processId, CancellationToken cancellationToken) =>
        Task.FromResult(Up(processId) is { } instance && HandshakeFailures-- <= 0
            ? new HandshakeResult { ProtocolVersion = 1, BridgeVersion = "0.1.0", Eplan = new() { ProcessId = processId }, AddinPath = ReportsPath ? instance.LoadedFrom : null }
            : null);

    private Instance? Up(int processId)
    {
        Instance? instance = _instances.FirstOrDefault(i => i.Id == processId);
        if (instance is null || instance.Exited || instance.LoadedFrom is null || PipeAfterLooks is not { } looks)
        {
            return null;
        }

        return ++instance.Looks >= looks ? instance : null;
    }

    [GeneratedRegex("^/Variant:\"(?<variant>[^\"]*)\" EplApiModuleAction /(?:register:\"(?<register>[^\"]*)\"|unregister:\"[^\"]*\")$")]
    private static partial Regex Action();

    internal sealed class Instance(FakeEplan eplan, int id, string? loadedFrom, int start) : IStartedEplan
    {
        private int _closeRequests;
        private bool _refreshed;

        /// <summary>The calls in order: "refresh", "close".</summary>
        public List<string> Calls { get; } = [];

        public int Looks { get; set; }

        public bool Exited { get; private set; }

        public bool Disposed { get; private set; }

        /// <summary>The DLL whose Bridge runs in this EPLAN, or null when none loads.</summary>
        public string? LoadedFrom => loadedFrom;

        public int Id => id;

        public bool HasExited => Exited |= eplan.ExitsBeforePipe || start >= (eplan.ExitsFromStart ?? int.MaxValue);

        public void Refresh()
        {
            Calls.Add("refresh");
            _refreshed = true;
        }

        public bool CloseMainWindow()
        {
            eplan.Log.Add($"close {id}");
            Calls.Add("close");
            Exited |= _refreshed && eplan.HasWindow && eplan.ClosesWhenAsked && ++_closeRequests > eplan.IgnoresCloses;
            return eplan.HasWindow;
        }

        public void Dispose() => Disposed = true;
    }
}
