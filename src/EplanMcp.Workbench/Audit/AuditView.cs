using System.ComponentModel;
using System.Globalization;
using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Commands;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Audit;

/// <summary>Where the audit page is: reading the log, showing it, or unable to read the folder (ERR-06).</summary>
internal enum AuditState
{
    Loading,
    Ready,
    Failed,
}

/// <summary>A choice of a filter: a value of the log, or null for all, with its text in the current language.</summary>
internal sealed record FilterOption(string? Value, string Text);

/// <summary>
/// The audit page without its window (M5 design 4.4 item 2, workbench-ui NAV-10): the log read as the Host reads it, the
/// filters by day, tool, project, decision and execution, and "verify integrity" through <c>audit verify</c>. Read only:
/// nothing here writes. Used on the UI thread; reading runs on a pool thread meanwhile (STS-03).
/// </summary>
internal sealed class AuditView(string directory, HostCommands host, TimeProvider clock) : INotifyPropertyChanged
{
    private const string DayFormat = "yyyy-MM-dd";

    /// <summary>The kinds, decisions and results the Host writes (WritePipeline, DanglingWrites), each with its own text.</summary>
    internal static readonly string[] Kinds = ["execution_started", "execution_result", "approval", "suspension_release", "writes_suspended"];

    internal static readonly string[] Decisions = ["granted", "denied", "cancelled", "expired", "unavailable"];

    internal static readonly string[] Executions = ["committed", "rolled_back", "not_started", "unknown"];

    private IReadOnlyList<AuditRecord> _all = [];
    private Values _values = Values.None;
    private string? _day;
    private string? _tool;
    private string? _project;
    private string? _decision;
    private string? _execution;
    private AuditVerifyReport? _verified;
    private HostCommandException? _verifyError;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The audit folder of the data root, which "open the folder" opens.</summary>
    public string Folder { get; } = directory;

    public AuditState State { get; private set; } = AuditState.Loading;

    /// <summary>How far reading has come, 0 to 1 (STS-03).</summary>
    public double Progress { get; private set; }

    /// <summary>Why the folder could not be read, for the error's details (ERR-02).</summary>
    public string? Problem { get; private set; }

    /// <summary>Lines that are not records; audit verify says where.</summary>
    public int NotRecords { get; private set; }

    /// <summary>The log has no record at all (ERR-05), as opposed to none that passes the filters.</summary>
    public bool IsEmpty => State == AuditState.Ready && _all.Count == 0;

    public int Total => _all.Count;

    /// <summary>The records that pass the filters, newest first.</summary>
    public IReadOnlyList<AuditRecord> Shown { get; private set; } = [];

    /// <summary>The day chosen, <c>yyyy-MM-dd</c> on this computer's clock; null for every day.</summary>
    public string? Day
    {
        get => _day;
        set => Filter(ref _day, value);
    }

    public string? Tool
    {
        get => _tool;
        set => Filter(ref _tool, value);
    }

    public string? Project
    {
        get => _project;
        set => Filter(ref _project, value);
    }

    public string? Decision
    {
        get => _decision;
        set => Filter(ref _decision, value);
    }

    public string? Execution
    {
        get => _execution;
        set => Filter(ref _execution, value);
    }

    /// <summary>Whether <c>audit verify</c> is running (STS-03).</summary>
    public bool IsVerifying { get; private set; }

    /// <summary>What the last <c>audit verify</c> found, or why it found nothing, in the current language; null before the first.</summary>
    public string? Verification => (_verifyError, _verified) switch
    {
        ({ } error, _) => HostErrors.Text(error),
        (_, null) => null,
        (_, { Exists: false } report) => Loc.Current.T("Audit.Verify.NoFolder", report.Directory),
        (_, { Break: { } at } report) => Loc.Current.T("Audit.Verify.Broken", at.File, at.Line, Loc.Current["Audit.Break." + at.Problem], report.IntactRecords),
        (_, { } report) => Loc.Current.T("Audit.Verify.Intact", report.IntactRecords),
    };

    /// <summary>Whether the last verification found the chain broken, or could not run.</summary>
    public bool VerificationFailed => _verifyError is not null || _verified is { Intact: false };

    /// <summary>The days that have records, newest first, in the current language's date format.</summary>
    public IReadOnlyList<FilterOption> DayOptions =>
        [new(null, Loc.Current["Audit.All"]), .. _values.Days.Select(d => new FilterOption(d, DateOnly.ParseExact(d, DayFormat, CultureInfo.InvariantCulture).ToString("d", CultureInfo.CurrentCulture)))];

    public IReadOnlyList<FilterOption> ToolOptions => Options(_values.Tools, v => v);

    public IReadOnlyList<FilterOption> ProjectOptions => Options(_values.Projects, v => v);

    public IReadOnlyList<FilterOption> DecisionOptions => Options(_values.Decisions, v => Label("Audit.Decision.", v));

    public IReadOnlyList<FilterOption> ExecutionOptions => Options(_values.Executions, v => Label("Audit.Execution.", v));

    /// <summary>
    /// A value's text: the catalogue's for a value the Host writes (<c>rolled_back</c> under <c>Audit.Execution.RolledBack</c>),
    /// the value itself, made plain, for any other.
    /// </summary>
    public static string Label(string prefix, string value) =>
        Catalogue(Key(prefix, value)) ?? ProjectText.Line(value);

    /// <summary>The catalogue key of a snake_case value the Host writes.</summary>
    internal static string Key(string prefix, string value) =>
        prefix + string.Concat(value.Split('_').Select(word => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..]));

    /// <summary>The record's local time on this computer.</summary>
    public DateTimeOffset? LocalTime(AuditRecord record) => record.Time is { } time ? TimeZoneInfo.ConvertTime(time, clock.LocalTimeZone) : null;

    /// <summary>The record's day on this computer, <c>yyyy-MM-dd</c>.</summary>
    public string? DayOf(AuditRecord record) => LocalTime(record)?.ToString(DayFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads the whole log again (F5); the filters stay, those that no longer match anything included. However reading
    /// ends, the page leaves "Loading": a log it cannot read is an error with the reason (ERR-04), never a page that
    /// reads for ever. One change is told when it starts and one when it ends; progress tells only itself.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        (State, Progress, Problem) = (AuditState.Loading, 0, null);
        Changed();
        var progress = new Progress<double>(done =>
        {
            Progress = done;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Progress)));
        });
        try
        {
            AuditLogContents contents = await Task.Run(() => AuditReader.Read(Folder, progress, cancellationToken), cancellationToken).ConfigureAwait(true);
            (_all, NotRecords, State) = ([.. contents.Records.OrderByDescending(r => r.Time ?? DateTimeOffset.MinValue)], contents.NotRecords, AuditState.Ready);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            (_all, NotRecords, State, Problem) = ([], 0, AuditState.Failed, ex.Message);
        }
        finally
        {
            if (State == AuditState.Loading)
            {
                (_all, State) = ([], AuditState.Failed);
            }

            _values = new Values(_all, DayOf);
            Shown = Filtered();
            Changed();
        }
    }

    /// <summary>
    /// Runs <c>audit verify</c> (NAV-10) and says what it found, and what a hash chain cannot show: records cut off at the
    /// end leave nothing behind them to notice it (architecture 8.8).
    /// </summary>
    public async Task VerifyAsync(CancellationToken cancellationToken)
    {
        IsVerifying = true;
        Changed();
        try
        {
            (_verified, _verifyError) = (await host.VerifyAuditAsync(cancellationToken).ConfigureAwait(true), null);
        }
        catch (HostCommandException ex)
        {
            (_verified, _verifyError) = (null, ex);
        }
        finally
        {
            IsVerifying = false;
            Changed();
        }
    }

    private static string? Catalogue(string key) => Localization.Catalogue.English.ContainsKey(key) ? Loc.Current[key] : null;

    private static IReadOnlyList<FilterOption> Options(IReadOnlyList<string> values, Func<string, string> text) =>
        [new(null, Loc.Current["Audit.All"]), .. values.Select(v => new FilterOption(v, text(v))).OrderBy(o => o.Text, StringComparer.CurrentCulture)];

    private void Filter<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        Shown = Filtered();
        Changed();
    }

    /// <summary>The records that pass the filters, worked out once per change and kept.</summary>
    private AuditRecord[] Filtered() =>
        [.. _all.Where(r => (_day is null || DayOf(r) == _day) && Is(r.Tool, _tool) && Is(r.Project, _project) && Is(r.Decision, _decision) && Is(r.Execution, _execution))];

    private static bool Is(string? value, string? chosen) => chosen is null || string.Equals(value, chosen, StringComparison.Ordinal);

    /// <summary>One notice that the view changed (an empty property name: all of it), for the page to show once.</summary>
    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    /// <summary>The distinct values the filters offer, worked out once per reading.</summary>
    private sealed record Values(IReadOnlyList<string> Days, IReadOnlyList<string> Tools, IReadOnlyList<string> Projects, IReadOnlyList<string> Decisions, IReadOnlyList<string> Executions)
    {
        public static readonly Values None = new([], [], [], [], []);

        public Values(IReadOnlyList<AuditRecord> records, Func<AuditRecord, string?> dayOf)
            : this(
                [.. records.Select(dayOf).OfType<string>().Distinct(StringComparer.Ordinal).OrderDescending(StringComparer.Ordinal)],
                Distinct(records.Select(r => r.Tool)),
                Distinct(records.Select(r => r.Project)),
                Distinct(records.Select(r => r.Decision)),
                Distinct(records.Select(r => r.Execution)))
        {
        }

        private static string[] Distinct(IEnumerable<string?> values) => [.. values.OfType<string>().Distinct(StringComparer.Ordinal)];
    }
}
