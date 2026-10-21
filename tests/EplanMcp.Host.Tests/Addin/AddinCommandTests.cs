using System.ComponentModel;
using System.Globalization;
using EplanMcp.Host.Addin;

namespace EplanMcp.Host.Tests.Addin;

/// <summary>
/// install-addin and uninstall-addin end to end in this process, over a temporary Program Files, Public folder, profile
/// and data root, with EPLAN faked as the live check found it (<see cref="FakeEplan"/>): what is started with which
/// command line, what is waited for, what is closed, and the record. No EPLAN.exe is ever started. Medium.
/// </summary>
[Trait("Size", "Medium")]
public sealed class AddinCommandTests : IDisposable
{
    private const string OldDll = @"D:\Code\EplanMcp\src\EplanMcp.Bridge.Eplan\bin\Release\net481\EplanMcp.Bridge.Eplan.dll";

    private static readonly AddinTimings _timings = new(TimeSpan.FromMilliseconds(200), TimeSpan.Zero, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1));

    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-addin-").FullName;
    private readonly FixedNow _clock = new(new DateTimeOffset(2026, 10, 21, 8, 15, 30, TimeSpan.Zero));
    private readonly FakeEplan _eplan = new();

    public AddinCommandTests()
    {
        InstallEplan("2026.0.3", "Electric P8", "Pro Panel");
        Directory.CreateDirectory(Path.Combine(ProgramFiles, "EPLAN", "Electric P8 Data"));
        Directory.CreateDirectory(Path.GetDirectoryName(Dll)!);
        File.WriteAllText(Dll, "add-in");
        File.WriteAllText(Dll + ZoneMark.Stream, "[ZoneTransfer]\r\nZoneId=3\r\n");
    }

    private string ProgramFiles => Path.Combine(_root, "Program Files");

    private string Profile => Path.Combine(_root, "Users", "工程师");

    private string App => Path.Combine(Profile, "EplanMcp", "app");

    private string Dll => Path.Combine(App, "bridge", "net481", "EplanMcp.Bridge.Eplan.dll");

    private string Eplan2026 => Exe("2026.0.3");

    private AddinRecord Record => AddinRecord.For(Locations);

    private HostDataLocations Locations => new(Path.Combine(Profile, "EplanMcp"));

    private AddinCommand Command => new(
        new(ProgramFiles, Path.Combine(_root, "Users", "Public"), Profile, App, exe => Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(exe))) + ".1"),
        Locations, _eplan, _eplan, _clock, _timings);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Install_EveryVariant_RegistersEachWithEplansCommandLineClosesItAndRecordsIt()
    {
        AddinReport report = await Command.InstallAsync([]);

        Assert.False(report.Failed);
        Assert.Equal(Dll, report.Dll);
        Assert.Equal(
            [
                $"start {Eplan2026} /Variant:\"Electric P8\" EplApiModuleAction /register:\"{Dll}\"",
                "close 4000",
                $"start {Eplan2026} /Variant:\"Pro Panel\" EplApiModuleAction /register:\"{Dll}\"",
                "close 4001",
            ],
            _eplan.Log);
        Assert.Equal(
            [("Electric P8", AddinOutcome.Registered, (int?)4000), ("Pro Panel", AddinOutcome.Registered, 4001)],
            report.Results.Select(r => (r.Variant, r.Outcome, r.ProcessId)));
        Assert.All(report.Results, r => Assert.Equal((Eplan2026, "2026.0.3.1", (AddinIssue?)null), (r.Eplan, r.Version, r.Problem)));
        Assert.All(_eplan.Instances, i => Assert.True(i.Disposed));
        Assert.Equal(
            [
                new(Eplan2026, "2026.0.3.1", "Electric P8", Dll, _clock.Now),
                new RecordedAddin(Eplan2026, "2026.0.3.1", "Pro Panel", Dll, _clock.Now),
            ],
            Record.All());
    }

    [Fact]
    public async Task Install_RemovesTheDownloadMarkFromEveryFileOfTheAddinFirst()
    {
        string other = Path.Combine(App, "bridge", "net481", "sub", "EplanMcp.Contracts.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        File.WriteAllText(other, "contracts");
        File.WriteAllText(other + ZoneMark.Stream, "[ZoneTransfer]\r\nZoneId=3\r\n");

        await Command.InstallAsync(["Electric P8"]);

        Assert.False(File.Exists(Dll + ZoneMark.Stream));
        Assert.False(File.Exists(other + ZoneMark.Stream));
        Assert.Equal(("add-in", "contracts"), (File.ReadAllText(Dll), File.ReadAllText(other)));
    }

    [Fact]
    public async Task Install_ChosenVariants_RegistersOnlyThoseUnderTheFoldersSpelling()
    {
        AddinReport report = await Command.InstallAsync(["pro panel"]);

        Assert.Equal([$"start {Eplan2026} /Variant:\"Pro Panel\" EplApiModuleAction /register:\"{Dll}\"", "close 4000"], _eplan.Log);
        Assert.Equal(["Pro Panel"], Record.All().Select(r => r.Variant));
        Assert.Equal("Pro Panel", report.Results.Single().Variant);
    }

    [Fact]
    public async Task Install_AVariantThatIsNotInstalled_RegistersNothingAndListsTheInstalledOnes()
    {
        AddinReport report = await Command.InstallAsync(["Electric P8", "Fluid"]);

        Assert.Equal(new AddinIssue(AddinProblem.VariantNotFound, "Fluid", "Electric P8, Pro Panel"), report.Problem);
        Assert.Empty(_eplan.Log);
        Assert.Empty(report.Results);
        Assert.False(File.Exists(Record.FilePath));
        Assert.True(File.Exists(Dll + ZoneMark.Stream));
    }

    [Fact]
    public async Task Install_WithoutEplan_RegistersNothing()
    {
        Directory.Delete(Path.Combine(ProgramFiles, "EPLAN"), recursive: true);

        AddinReport report = await Command.InstallAsync([]);

        Assert.Equal(new AddinIssue(AddinProblem.NoEplan, Path.Combine(ProgramFiles, "EPLAN")), report.Problem);
        Assert.Empty(_eplan.Log);
    }

    [Fact]
    public async Task Install_WithoutTheAddin_SaysWhereItLookedAndStartsNothing()
    {
        File.Delete(Dll);

        AddinReport report = await Command.InstallAsync([]);

        Assert.Equal((AddinProblem.AddinNotFound, Dll), (report.Problem!.Problem, report.Problem.Detail));
        Assert.Null(report.Dll);
        Assert.Empty(_eplan.Log);
    }

    [Fact]
    public async Task Install_SkipsAVersionOutsideTheRangeAndNotesOneNotTestedYet()
    {
        InstallEplan("2023.0.3", "Electric P8");
        InstallEplan("2025.0.3", "Electric P8");

        AddinReport report = await Command.InstallAsync(["Electric P8"]);

        Assert.False(report.Failed);
        Assert.Equal(
            [
                ("2023.0.3.1", (string?)null, AddinOutcome.Skipped, AddinNote.UnsupportedVersion),
                ("2025.0.3.1", "Electric P8", AddinOutcome.Registered, AddinNote.VersionUntested),
            ],
            report.Results.Where(r => r.Notes.Count > 0).Select(r => (r.Version, r.Variant, r.Outcome, r.Notes.Single())));
        Assert.Equal([Exe("2025.0.3"), Eplan2026], Record.All().Select(r => r.Eplan));
        Assert.DoesNotContain(_eplan.Log, l => l.Contains("2023.0.3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Install_WhileAnyEplanRuns_RefusesEveryVariant()
    {
        // Which variant a running EPLAN is cannot be told: its registry entry names the licence (api-notes A1).
        _eplan.Others.AddRange([1234, 5678]);

        AddinReport report = await Command.InstallAsync([]);

        Assert.All(report.Results, r => Assert.Equal((AddinOutcome.Refused, new AddinIssue(AddinProblem.EplanRunning, "1234, 5678"), (int?)null), (r.Outcome, r.Problem, r.ProcessId)));
        Assert.Equal(2, report.Results.Count);
        Assert.Empty(_eplan.Log);
        Assert.False(File.Exists(Record.FilePath));
    }

    [Fact]
    public async Task Install_WhenThisDllIsRegisteredAlready_TakesOneStart()
    {
        _eplan.Registered["Electric P8"] = Dll;

        AddinReport report = await Command.InstallAsync(["Electric P8"]);

        Assert.Equal((AddinOutcome.Registered, (AddinIssue?)null), (report.Results.Single().Outcome, report.Results.Single().Problem));
        Assert.Equal([$"start {Eplan2026} /Variant:\"Electric P8\" EplApiModuleAction /register:\"{Dll}\"", "close 4000"], _eplan.Log);
    }

    [Fact]
    public async Task Install_OverAnotherPath_UnregistersItInAnEplanOfItsOwnThenRegistersInAThird()
    {
        // Live check: the add-in registered at the old path loads at start-up, and /register of the new one does not take.
        _eplan.Registered["Electric P8"] = OldDll;

        AddinReport report = await Command.InstallAsync(["Electric P8"]);

        Assert.Equal(
            [
                $"start {Eplan2026} /Variant:\"Electric P8\" EplApiModuleAction /register:\"{Dll}\"",
                "close 4000",
                $"start {Eplan2026} /Variant:\"Electric P8\" EplApiModuleAction /unregister:\"EplanMcp.Bridge.Eplan\"",
                "close 4001",
                $"start {Eplan2026} /Variant:\"Electric P8\" EplApiModuleAction /register:\"{Dll}\"",
                "close 4002",
            ],
            _eplan.Log);
        AddinResult result = report.Results.Single();
        Assert.Equal((AddinOutcome.Registered, (AddinIssue?)null, (int?)4002), (result.Outcome, result.Problem, result.ProcessId));
        Assert.Equal(Dll, _eplan.Registered["Electric P8"]);
        Assert.Equal([Dll], Record.All().Select(r => r.Dll));
    }

    [Fact]
    public async Task Install_OverAnOlderAddinThatDoesNotSayItsPath_RegistersAgainAndTrustsTheFreshStart()
    {
        // Second live check: the add-in registered from the main checkout was built before addinPath existed.
        _eplan.Registered["Electric P8"] = OldDll;
        _eplan.ReportsPath = false;

        AddinReport report = await Command.InstallAsync(["Electric P8"]);

        Assert.Equal(
            [
                $"start {Eplan2026} /Variant:\"Electric P8\" EplApiModuleAction /register:\"{Dll}\"",
                "close 4000",
                $"start {Eplan2026} /Variant:\"Electric P8\" EplApiModuleAction /unregister:\"EplanMcp.Bridge.Eplan\"",
                "close 4001",
                $"start {Eplan2026} /Variant:\"Electric P8\" EplApiModuleAction /register:\"{Dll}\"",
                "close 4002",
            ],
            _eplan.Log);
        Assert.All(_eplan.Instances, i => Assert.Equal(["refresh", "close"], i.Calls));
        AddinResult result = report.Results.Single();
        Assert.Equal((AddinOutcome.Registered, (AddinIssue?)null, (int?)4002, 0), (result.Outcome, result.Problem, result.ProcessId, result.Notes.Count));
        Assert.Equal(Dll, _eplan.Registered["Electric P8"]);
        Assert.Equal([Dll], Record.All().Select(r => r.Dll));
    }

    [Fact]
    public async Task Install_WhenTheEplanWithAnOlderAddinDoesNotClose_SaysThePathIsUnknown()
    {
        _eplan.Registered["Electric P8"] = OldDll;
        _eplan.ReportsPath = false;
        _eplan.HasWindow = false;

        AddinReport report = await Command.InstallAsync(["Electric P8"]);

        AddinResult result = report.Results.Single();
        Assert.Equal((AddinOutcome.NotConfirmed, new AddinIssue(AddinProblem.PathMismatch, string.Empty), AddinNote.CloseEplan), (result.Outcome, result.Problem, result.Notes.Single()));
        Assert.Single(_eplan.Instances);
    }

    [Fact]
    public async Task Install_WhenTheOldPathStillLoadsAfterUnregistering_IsNotConfirmedAndNotRecorded()
    {
        _eplan.Registered["Electric P8"] = OldDll;
        _eplan.UnregisterTakes = false;

        AddinReport report = await Command.InstallAsync(["Electric P8"]);

        AddinResult result = report.Results.Single();
        Assert.Equal((AddinOutcome.NotConfirmed, new AddinIssue(AddinProblem.PathMismatch, OldDll)), (result.Outcome, result.Problem));
        Assert.Equal(3, _eplan.Instances.Count);
        Assert.False(File.Exists(Record.FilePath));
    }

    [Fact]
    public async Task Install_WhenTheEplanWithTheOldRegistrationDoesNotClose_GoesNoFurther()
    {
        _eplan.Registered["Electric P8"] = OldDll;
        _eplan.HasWindow = false;

        AddinReport report = await Command.InstallAsync(["Electric P8"]);

        AddinResult result = report.Results.Single();
        Assert.Equal((AddinOutcome.NotConfirmed, new AddinIssue(AddinProblem.PathMismatch, OldDll), AddinNote.CloseEplan), (result.Outcome, result.Problem, result.Notes.Single()));
        Assert.Single(_eplan.Instances);
        Assert.False(File.Exists(Record.FilePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Install_WhenTheBridgeDoesNotAnswerTheHandshake_GoesNoFurtherButRecords(bool registeredElsewhere)
    {
        // Which registration loaded cannot be told; undoing it might undo another user's (Q15).
        if (registeredElsewhere)
        {
            _eplan.Registered["Electric P8"] = OldDll;
        }

        _eplan.HandshakeFailures = int.MaxValue;

        AddinReport report = await Command.InstallAsync(["Electric P8"]);

        Assert.Equal((AddinOutcome.NotConfirmed, new AddinIssue(AddinProblem.HandshakeFailed, "4000")), (report.Results.Single().Outcome, report.Results.Single().Problem));
        Assert.Single(_eplan.Instances);
        Assert.Equal([Dll], Record.All().Select(r => r.Dll));
    }

    [Fact]
    public async Task Install_WhenTheEplanThatUnregistersExits_FailsAndKeepsTheRecord()
    {
        _eplan.Registered["Electric P8"] = OldDll;
        Record.Add(new(Eplan2026, "2026.0.3.1", "Electric P8", OldDll, _clock.Now));
        _eplan.ExitsFromStart = 1;

        AddinReport report = await Command.InstallAsync(["Electric P8"]);

        Assert.Equal((AddinOutcome.Failed, new AddinIssue(AddinProblem.EplanExited, "4001")), (report.Results.Single().Outcome, report.Results.Single().Problem));
        Assert.Equal(2, _eplan.Instances.Count);
        Assert.Equal([OldDll], Record.All().Select(r => r.Dll));
    }

    [Fact]
    public async Task Install_WhenTheEplanThatRegistersAgainExits_ForgetsTheUndoneRegistration()
    {
        _eplan.Registered["Electric P8"] = OldDll;
        Record.Add(new(Eplan2026, "2026.0.3.1", "Electric P8", OldDll, _clock.Now));
        _eplan.ExitsFromStart = 2;

        AddinReport report = await Command.InstallAsync(["Electric P8"]);

        Assert.Equal((AddinOutcome.Failed, new AddinIssue(AddinProblem.EplanExited, "4002")), (report.Results.Single().Outcome, report.Results.Single().Problem));
        Assert.Equal(3, _eplan.Instances.Count);
        Assert.Empty(Record.All());
    }

    [Fact]
    public async Task Install_WithOnlyVersionsOutsideTheRange_NamesThem()
    {
        Directory.Delete(Path.Combine(ProgramFiles, "EPLAN"), recursive: true);
        InstallEplan("2023.0.3", "Electric P8");

        AddinReport report = await Command.InstallAsync([]);

        Assert.Equal(new AddinIssue(AddinProblem.NoEplan, Path.Combine(ProgramFiles, "EPLAN"), "2023.0.3.1"), report.Problem);
        Assert.Empty(_eplan.Log);
    }

    [Fact]
    public async Task Install_AsksEplanToCloseAgainUntilItDoes()
    {
        // Each request goes out after a refresh (second live check: Process kept the splash screen's handle).
        _eplan.IgnoresCloses = 2;

        AddinReport report = await Command.InstallAsync(["Electric P8"]);

        Assert.Equal((AddinOutcome.Registered, 0), (report.Results.Single().Outcome, report.Results.Single().Notes.Count));
        Assert.Equal(["close 4000", "close 4000", "close 4000"], _eplan.Log.Skip(1));
        Assert.True(_eplan.Instances.Single().Exited);
    }
    [Fact]
    public async Task Install_WhenEplanDoesNotLoadTheAddinInTime_LeavesItOpenAndRecordsTheRegistration()
    {
        // A sign-in or licence dialog: the add-in loads only after the engineer is through it.
        _eplan.PipeAfterLooks = null;

        AddinReport report = await Command.InstallAsync(["Electric P8"]);

        AddinResult result = report.Results.Single();
        Assert.Equal((AddinOutcome.NotConfirmed, new AddinIssue(AddinProblem.NotLoaded, "4000")), (result.Outcome, result.Problem));
        Assert.DoesNotContain(_eplan.Log, l => l.StartsWith("close", StringComparison.Ordinal));
        Assert.False(_eplan.Instances.Single().Exited);
        Assert.Equal(["Electric P8"], Record.All().Select(r => r.Variant));
    }

    [Fact]
    public async Task Install_WhenEplanExitsBeforeLoadingTheAddin_FailsAndRecordsNothing()
    {
        _eplan.PipeAfterLooks = null;
        _eplan.ExitsBeforePipe = true;

        AddinReport report = await Command.InstallAsync(["Electric P8"]);

        AddinResult result = report.Results.Single();
        Assert.Equal((AddinOutcome.Failed, new AddinIssue(AddinProblem.EplanExited, "4000")), (result.Outcome, result.Problem));
        Assert.False(File.Exists(Record.FilePath));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Install_WhenEplanDoesNotClose_IsRegisteredAndAsksTheEngineerToCloseIt(bool hasWindow, bool closesWhenAsked)
    {
        _eplan.HasWindow = hasWindow;
        _eplan.ClosesWhenAsked = closesWhenAsked;

        AddinReport report = await Command.InstallAsync(["Electric P8"]);

        Assert.False(report.Failed);
        AddinResult result = report.Results.Single();
        Assert.Equal((AddinOutcome.Registered, AddinNote.CloseEplan), (result.Outcome, result.Notes.Single()));
        Assert.False(_eplan.Instances.Single().Exited);
        Assert.Single(Record.All());
    }

    [Fact]
    public async Task Install_WhenEplanCannotStart_Fails()
    {
        _eplan.StartFails = new Win32Exception(2, "The system cannot find the file specified.");

        AddinReport report = await Command.InstallAsync(["Electric P8"]);

        Assert.Equal(new AddinIssue(AddinProblem.StartFailed, "The system cannot find the file specified."), report.Results.Single().Problem);
        Assert.False(File.Exists(Record.FilePath));
    }

    [Fact]
    public async Task Install_WithAnUnreadableRecord_StartsNothing()
    {
        Directory.CreateDirectory(Locations.State);
        File.WriteAllText(Record.FilePath, "not json");

        AddinReport report = await Command.InstallAsync([]);

        Assert.Equal(AddinProblem.InvalidRecord, report.Problem!.Problem);
        Assert.Empty(_eplan.Log);
        Assert.Equal("not json", File.ReadAllText(Record.FilePath));
    }

    [Fact]
    public async Task Uninstall_UnregistersEachRecordedVariantAndForgetsIt()
    {
        await Command.InstallAsync([]);
        _eplan.Log.Clear();

        AddinReport report = await Command.UninstallAsync();

        Assert.False(report.Failed);
        Assert.Equal(
            [
                $"start {Eplan2026} /Variant:\"Electric P8\" EplApiModuleAction /unregister:\"EplanMcp.Bridge.Eplan\"",
                "close 4002",
                $"start {Eplan2026} /Variant:\"Pro Panel\" EplApiModuleAction /unregister:\"EplanMcp.Bridge.Eplan\"",
                "close 4003",
            ],
            _eplan.Log);
        Assert.All(report.Results, r => Assert.Equal((AddinOutcome.Unregistered, (AddinIssue?)null), (r.Outcome, r.Problem)));
        Assert.Empty(Record.All());
    }

    [Fact]
    public async Task Uninstall_ARecordWhoseEplanOrVariantIsGone_IsDroppedWithoutStartingAnything()
    {
        Record.Add(new(Exe("2024.0.3"), "2024.0.3.1", "Electric P8", Dll, _clock.Now));
        Record.Add(new(Eplan2026, "2026.0.3.1", "Fluid", Dll, _clock.Now));
        Record.Add(new(@"C:\Windows\System32\cmd.exe", "1", "Electric P8", Dll, _clock.Now));

        AddinReport report = await Command.UninstallAsync();

        Assert.False(report.Failed);
        Assert.All(report.Results, r => Assert.Equal((AddinOutcome.Skipped, AddinNote.EplanMissing), (r.Outcome, r.Notes.Single())));
        Assert.Empty(_eplan.Log);
        Assert.Empty(Record.All());
    }

    [Fact]
    public async Task Uninstall_WhileAnyEplanRuns_RefusesAndKeepsTheRecord()
    {
        Record.Add(new(Eplan2026, "2026.0.3.1", "Pro Panel", Dll, _clock.Now));
        _eplan.Others.Add(1234);

        AddinReport report = await Command.UninstallAsync();

        Assert.Equal(new AddinIssue(AddinProblem.EplanRunning, "1234"), report.Results.Single().Problem);
        Assert.Empty(_eplan.Log);
        Assert.Single(Record.All());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Uninstall_NotConfirmed_KeepsTheRecordForAnotherTry(bool exits)
    {
        Record.Add(new(Eplan2026, "2026.0.3.1", "Pro Panel", Dll, _clock.Now));
        _eplan.PipeAfterLooks = null;
        _eplan.ExitsBeforePipe = exits;

        AddinReport report = await Command.UninstallAsync();

        AddinResult result = report.Results.Single();
        Assert.Equal(exits ? (AddinOutcome.Failed, AddinProblem.EplanExited) : (AddinOutcome.NotConfirmed, AddinProblem.NotLoaded), (result.Outcome, result.Problem!.Problem));
        Assert.DoesNotContain(_eplan.Log, l => l.StartsWith("close", StringComparison.Ordinal));
        Assert.Single(Record.All());
    }

    [Fact]
    public async Task Uninstall_WithNothingRecorded_DoesNothing()
    {
        AddinReport report = await Command.UninstallAsync();

        Assert.Equal((false, 0), (report.Failed, report.Results.Count));
        Assert.Empty(_eplan.Log);
    }

    [Fact]
    public async Task Run_SaysOnStderrBeforeEachStartOfEplan()
    {
        _eplan.Registered["Electric P8"] = OldDll;
        using var output = new StringWriter();
        using var progress = new StringWriter();

        int exit = await Command.RunAsync(AddinAction.Install, ["Electric P8"], output, progress, CultureInfo.GetCultureInfo("en-US"), json: false);

        Assert.Equal(0, exit);
        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "EPLAN 2026.0.3.1 Electric P8: starting EPLAN to register the add-in; this can take up to 0.2 s...",
                "EPLAN 2026.0.3.1 Electric P8: starting EPLAN to unregister the add-in; this can take up to 0.2 s...",
                "EPLAN 2026.0.3.1 Electric P8: starting EPLAN to register the add-in; this can take up to 0.2 s...",
                string.Empty),
            progress.ToString());
        Assert.Equal("add-in: " + Dll + Environment.NewLine + "[registered] EPLAN 2026.0.3.1 Electric P8 (process 4002)  " + Eplan2026 + Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Run_ExitsWith1WhenAnythingFailed()
    {
        _eplan.Others.Add(1234);
        using var output = new StringWriter();

        int exit = await Command.RunAsync(AddinAction.Install, [], output, TextWriter.Null, CultureInfo.GetCultureInfo("en-US"), json: false);

        Assert.Equal(1, exit);
        Assert.Contains("[refused] EPLAN 2026.0.3.1 Pro Panel", output.ToString(), StringComparison.Ordinal);
    }
    private string Exe(string folder) => Path.Combine(ProgramFiles, "EPLAN", "Platform", folder, "Bin", "EPLAN.exe");

    private void InstallEplan(string folder, params string[] variants)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Exe(folder))!);
        File.WriteAllText(Exe(folder), string.Empty);
        foreach (string variant in variants)
        {
            string bin = Path.Combine(ProgramFiles, "EPLAN", variant, folder, "Bin");
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(bin, "W3u.exe"), string.Empty);
        }
    }

    /// <summary>The real clock and timers, with a fixed time of day for the record.</summary>
    private sealed class FixedNow(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now => now;

        public override DateTimeOffset GetUtcNow() => now;
    }
}
