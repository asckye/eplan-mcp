using System.ComponentModel;
using System.Globalization;
using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Commands;
using EplanMcp.Workbench.Localization;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Status;

/// <summary>Where the status page is: not run yet, running doctor, showing its result, or without one (ERR-06).</summary>
internal enum StatusState
{
    Idle,
    Running,
    Ready,
    Failed,
}

/// <summary>
/// One line of the status page: a doctor check, or the Workbench's own line about the next approval. The text is the
/// Host's in the current language, made plain (CTL-12); the severity is in words as well as the icon (ERR-07).
/// </summary>
internal sealed class StatusLine(CheckStatus status, string zh, string en) : IAccessibleItem
{
    public CheckStatus Status { get; } = status;

    public string Text => ProjectText.Line(Loc.Current.Language == AppLanguage.Chinese ? zh : en);

    public string Severity => Loc.Current["Status." + Status];

    public string AccessibleName => Loc.Current.T("Status.LineName", Severity, Text);
}

/// <summary>A group of lines under its heading (M5 design 4.4 item 4).</summary>
internal sealed record StatusGroup(string Key, IReadOnlyList<StatusLine> Lines)
{
    public string Title => Loc.Current[Key];
}

internal sealed class VariantChoice(string name)
{
    public string Name { get; } = name;

    public string Text => ProjectText.Line(Name);

    public bool IsSelected { get; set; }
}

/// <summary>
/// The status page without its window (M5 design 4.4 item 4, workbench-ui section 7): <c>eplanmcp doctor</c>, run when
/// the page opens and on "Refresh", never polled, its checks grouped by their stable IDs (architecture 5.2); write
/// suspensions also as a Banner (STS-06); and which channel the next approval goes to, which this Workbench knows itself.
/// </summary>
/// <param name="host">The Host commands, for doctor.</param>
/// <param name="accepting">Whether this Workbench takes approval questions now: its pipe and a free place (STS-11).</param>
internal sealed class StatusView(HostCommands host, Func<bool> accepting) : INotifyPropertyChanged
{
    /// <summary>The groups in the design's order, each with the check IDs it shows; an ID not listed goes to "Other".</summary>
    internal static readonly (string Key, string[] Ids)[] Layout =
    [
        ("Status.Group.Eplan", ["eplan.installation"]),
        ("Status.Group.Running", ["eplan.running"]),
        ("Status.Group.AddIn", ["bridge.pipe", "bridge.handshake"]),
        ("Status.Group.Licence", ["bridge.licence"]),
        ("Status.Group.Suspensions", ["suspension"]),
        ("Status.Group.Approvals", []),
        ("Status.Group.Data", ["data.directory", "config", "audit.directory"]),
        ("Status.Group.Space", ["exports", "backups"]),
        ("Status.Group.Environment", ["msix", "path.length", "host.version"]),
    ];

    private DoctorReport? _report;
    private HostCommandException? _error;
    private AddinReport? _addins;

    public IReadOnlyList<VariantChoice> Variants { get; private set; } = [];

    public string Activity { get; private set; } = "Status.Running";

    public event PropertyChangedEventHandler? PropertyChanged;

    public StatusState State { get; private set; }

    public bool CanRun => host.IsAvailable;

    /// <summary>What a doctor that gave no result says to do (ERR-01), and its details (ERR-02).</summary>
    public string? Error => _error is { } error ? HostErrors.Text(error) : null;

    public string? ErrorDetails => _error is { } error ? HostErrors.Details(error) : null;

    /// <summary>The worst status of all, in words: the page's caption.</summary>
    public string? Summary => _addins is not null ? Loc.Current["Addin.Done"] : _report is { } report ? Loc.Current.T("Status.Summary", ProjectText.Line(report.HostVersion), Loc.Current["Status.Overall." + report.Status]) : null;

    /// <summary>The write suspensions doctor found, for the Banner: writes wait for the engineer's confirmation (ADR-0010).</summary>
    public IReadOnlyList<StatusLine> Suspensions => Lines("suspension").Where(l => l.Status is CheckStatus.Warning or CheckStatus.Failure).ToList();

    public IReadOnlyList<StatusGroup> Groups
    {
        get
        {
            if (_report is null && _addins is null)
            {
                return [];
            }

            string[] known = [.. Layout.SelectMany(g => g.Ids)];
            IEnumerable<StatusGroup> groups = Layout.Select(g => new StatusGroup(g.Key, g.Key == "Status.Group.Approvals" ? [NextApproval()] : [.. g.Ids.SelectMany(Lines)]));
            StatusLine[] other = [.. (_report?.Checks ?? []).Where(c => !known.Contains(c.Id)).Select(Line)];
            return [.. groups.Append(new StatusGroup("Status.Group.Other", other)).Append(new StatusGroup("Addin.Results", AddinLines())).Where(g => g.Lines.Count > 0)];
        }
    }

    /// <summary>Runs doctor once; while it runs, the page shows that it does (STS-03).</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        State = StatusState.Running;
        Activity = "Status.Running";
        _addins = null;
        Changed();
        try
        {
            (_report, _error) = (await host.DoctorAsync(cancellationToken).ConfigureAwait(true), null);
            Variants = [.. (await host.VariantsAsync(cancellationToken).ConfigureAwait(true)).Variants.Select(v => Variants.FirstOrDefault(c => c.Name == v) ?? new VariantChoice(v))];
            State = StatusState.Ready;
        }
        catch (HostCommandException ex)
        {
            (_report, _error, State) = (null, ex, StatusState.Failed);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            (_report, _error, State) = (null, new HostCommandException(HostFailure.Unreadable, ex.Message, ex), StatusState.Failed);
        }
        finally
        {
            // However it ends, the page leaves "Running": Refresh is never off for good.
            if (State == StatusState.Running)
            {
                (_report, State) = (null, StatusState.Failed);
            }

            Changed();
        }
    }

    public async Task RegisterAsync(bool uninstall, CancellationToken cancellationToken)
    {
        string[] selected = [.. Variants.Where(v => v.IsSelected).Select(v => v.Name)];
        if (!CanRun || State == StatusState.Running || (!uninstall && selected.Length == 0))
        {
            return;
        }

        (State, Activity, _error, _addins) = (StatusState.Running, "Addin.Running", null, null);
        Changed();
        try
        {
            _addins = await host.RegisterAsync(selected, uninstall, cancellationToken).ConfigureAwait(true);
            State = StatusState.Ready;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            (_error, State) = (ex as HostCommandException ?? new HostCommandException(HostFailure.Unreadable, ex.Message, ex), StatusState.Failed);
        }
        finally
        {
            Changed();
        }
    }

    private List<StatusLine> AddinLines()
    {
        var lines = new List<StatusLine>();
        if (_addins is { Problem: null, Results.Count: 0 })
        {
            lines.Add(new(CheckStatus.Info, Catalogue.Chinese["Addin.Empty"], Catalogue.English["Addin.Empty"]));
        }

        if (_addins?.Problem is { } problem)
        {
            lines.Add(new(CheckStatus.Failure, problem.Zh, problem.En));
        }

        foreach (AddinResult result in _addins?.Results ?? [])
        {
            string Text(IReadOnlyDictionary<string, string> texts, bool zh) => string.Format(CultureInfo.CurrentCulture, texts["Addin.Line"], result.Version, result.Variant, texts["Addin.Result." + result.Result])
                + " " + string.Join(" ", result.Notes.Concat(result.Problem is { } issue ? [issue] : []).Select(t => zh ? t.Zh : t.En));
            CheckStatus status = result.Result switch
            {
                AddinResultKind.NotConfirmed => CheckStatus.Warning,
                AddinResultKind.Failed or AddinResultKind.Refused => CheckStatus.Failure,
                AddinResultKind.Skipped => CheckStatus.Info,
                _ => result.Problem is null ? CheckStatus.Ok : CheckStatus.Failure,
            };
            lines.Add(new(status, Text(Catalogue.Chinese, true), Text(Catalogue.English, false)));
        }

        return lines;
    }

    /// <summary>The Host asks this Workbench while it takes questions, else the EPLAN dialog, else refuses (ADR-0014).</summary>
    private StatusLine NextApproval() => accepting()
        ? new(CheckStatus.Ok, Catalogue.Chinese["Status.NextWorkbench"], Catalogue.English["Status.NextWorkbench"])
        : new(CheckStatus.Warning, Catalogue.Chinese["Status.NextDialog"], Catalogue.English["Status.NextDialog"]);

    private IEnumerable<StatusLine> Lines(string id) => _report?.Checks.Where(c => c.Id == id).Select(Line) ?? [];

    private static StatusLine Line(DoctorCheck check) => new(check.Status, check.Zh, check.En);

    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
