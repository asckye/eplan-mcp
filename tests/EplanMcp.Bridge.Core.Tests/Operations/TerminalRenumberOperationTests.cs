using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// terminal.renumber.plan and terminal.renumber.execute against a fake that numbers the way spike S21 found EPLAN
/// numbers the strip +A1-XD1 of the sample project (api-notes A24): the scheme check, what counts as unnumbered, the
/// scope, the change list, the backup before the transaction, and the read-back by terminal. Medium: the backup goes
/// into a real temporary folder.
/// </summary>
[Trait("Size", "Medium")]
public sealed class TerminalRenumberOperationTests : IDisposable
{
    private const string Strip = "+A1-XD1";
    private const string Other = "+A1-XD4";

    private static readonly OpenProject _project = new() { Name = "Line1", Path = @"D:\Scratch\Line1" };

    private readonly string _folder = Directory.CreateTempSubdirectory("eplanmcp-terminals-").FullName;
    private readonly FakePageReader _pages = new();
    private readonly FakeTerminalRenumberer _renumberer = new();
    private readonly FakeProjectBackup _backup;
    private readonly FakeTransactions _transactions = new();

    public TerminalRenumberOperationTests()
    {
        _pages.Pages.Add(new PageSummary { Name = "=GAA+A1&EFS1/1" });
        _backup = new FakeProjectBackup(_pages);
        _renumberer.Strips[Strip] = [.. Enumerable.Range(1, 5).Select(d => Terminal(Strip + ":" + d.ToString(System.Globalization.CultureInfo.InvariantCulture), "x" + d.ToString(System.Globalization.CultureInfo.InvariantCulture)))];
        _renumberer.Strips[Other] = [Terminal(Other + ":1", "o1"), Terminal(Other + ":?1", "o2"), Terminal(Other + ":?2", "o3")];

        // S21: four terminals of +A1-XD1 took each other's numbers in the strip's order.
        _renumberer.Numbers[Strip + ":2"] = Strip + ":3";
        _renumberer.Numbers[Strip + ":3"] = Strip + ":5";
        _renumberer.Numbers[Strip + ":4"] = Strip + ":2";
        _renumberer.Numbers[Strip + ":5"] = Strip + ":4";
        _renumberer.Numbers[Other + ":?1"] = Other + ":2";
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("+A1-XD1:?1", true)]
    [InlineData("+A1-XD1:1?", true)]
    [InlineData("+A1-?XD1:1", false)] // the strip's own '?' is no terminal's (tool spec 5)
    [InlineData("+A1-XD1:1", false)]
    [InlineData("+A1-XD1", false)]
    [InlineData("", false)]
    public void Unnumbered_LooksAfterTheColon(string name, bool unnumbered)
    {
        Assert.Equal(unnumbered, TerminalRenumberRequest.Unnumbered(name));
    }

    [Fact]
    public void Plan_AllTerminalsOfAStrip_InTheScheme()
    {
        WritePlan plan = Plan(new TerminalRenumberArgs { Project = "Line1", Strips = [Strip], Scheme = " Numerisch ", OnlyUnnumbered = false });

        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("update", "terminals", "Line1", "numbering", null), (change.Action, change.Kind, change.Name, change.Property, change.NewValue));
        Assert.Equal("all terminal designations on 1 strip (5 terminals), scheme: Numerisch, start 1, step 1", change.OldValue);
        Assert.Equal(("5", "1", "false", "Numerisch", "1", "1"), (plan.Details!["terminalCount"], plan.Details["stripCount"], plan.Details["onlyUnnumbered"], plan.Details["scheme"], plan.Details["start"], plan.Details["step"]));
        Assert.Empty(_renumberer.Renumbers);
    }

    [Fact]
    public void Plan_OnlyUnnumbered_InTheProject_CountsTheTerminalsWithQuestionMarks()
    {
        WritePlan plan = Plan(new TerminalRenumberArgs { Project = "Line1", Scheme = "Numerisch", Start = 10, Step = 5 });

        Assert.Equal("terminal designations containing '?' in the project (2 terminals), scheme: Numerisch, start 10, step 5", Assert.Single(plan.Changes).OldValue);
        Assert.Equal((string.Empty, "2"), (plan.Details!["stripCount"], plan.Details["terminalCount"]));
    }

    [Fact]
    public void Plan_WithNothingToNumber_HasNoChange()
    {
        WritePlan plan = Plan(new TerminalRenumberArgs { Project = "Line1", Strips = [Strip], Scheme = "Numerisch" });

        Assert.Empty(plan.Changes);
        Assert.Equal("0", plan.Details!["terminalCount"]);
    }

    [Theory]
    [InlineData("numerisch")]
    [InlineData(SchemeChoice.Internal)]
    public void Plan_ASchemeEplanDoesNotOffer_IsInvalidWithTheOfferedOnes(string scheme)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(new TerminalRenumberArgs { Project = "Line1", Scheme = scheme })).Error;

        Assert.Equal((ErrorReason.InvalidArgument, scheme), (error.Reason, error.Metadata!["scheme"]));
        Assert.Equal("Gerätedefinition\nInitiatoren\nNumerisch", error.Metadata["candidates"]);
        Assert.Equal($"EPLAN has no terminal numbering scheme '{scheme}', so nothing was numbered. Use one of metadata.candidates; letter case counts.", error.Message);
    }

    [Fact]
    public void Plan_NamesEveryMissingStrip_AndEveryArgumentProblemAtOnce()
    {
        BridgeError missing = Assert.Throws<BridgeErrorException>(() => Plan(new TerminalRenumberArgs { Project = "Line1", Strips = ["+a1-xd1", Strip, "-XD9"], Scheme = "Numerisch" })).Error;
        BridgeError invalid = Assert.Throws<BridgeErrorException>(() => Plan(new TerminalRenumberArgs { Strips = [Strip, Strip, " "], Start = 0, Step = 0 })).Error;
        BridgeError empty = Assert.Throws<BridgeErrorException>(() => Plan(new TerminalRenumberArgs { Project = "Line1", Strips = [], Scheme = "Numerisch" })).Error;

        Assert.Equal((ErrorReason.DeviceNotFound, "+a1-xd1\n-XD9"), (missing.Reason, missing.Metadata!["missing"]));
        Assert.Equal(
            "'project' is required for writes: a name or path from eplan_project_list.\nstrips[1] repeats strips[0].\nstrips[2] is empty; give a strip name such as +A1-XD1 (eplan_terminal_strip_list).\n'scheme' is required: a terminal numbering scheme such as Numerisch.\n'start' must be at least 1.\n'step' must be at least 1.",
            invalid.Metadata!["violations"]);
        Assert.Equal("'strips' must hold 1 to 50 terminal strip names; omit it for the whole project.", empty.Metadata!["violations"]);
    }

    [Fact]
    public void Execute_BacksUpFirst_ThenNumbersInOneTransaction_AndReportsByTerminal()
    {
        var args = new TerminalRenumberArgs { Project = "Line1", Strips = [Strip], Scheme = "Numerisch", OnlyUnnumbered = false };
        args.Fingerprint = Plan(args).Fingerprint;
        _transactions.Snapshot = () =>
        {
            Assert.Single(_backup.Copies);
            return () => { };
        };

        TerminalRenumberData data = Execute(args);

        var call = Assert.Single(_renumberer.Renumbers);
        Assert.Equal([Strip], call.Strips!);
        Assert.Equal((false, "Numerisch", 1, 1), (call.OnlyUnnumbered, call.Scheme, call.Start, call.Step));
        Assert.Equal(["Begin AI: eplan_terminal_renumber", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
        Assert.Equal([(":2", ":3"), (":3", ":5"), (":4", ":2"), (":5", ":4")], data.Changed.Select(c => (c.From[Strip.Length..], c.To[Strip.Length..])));
        Assert.Equal((4, 0, null), (data.ChangedCount, data.StillUnnumberedCount, data.Warning));
    }

    [Fact]
    public void Execute_OnlyUnnumbered_InTheProject_ListsWhatStillHasQuestionMarks()
    {
        var args = new TerminalRenumberArgs { Project = "Line1", Scheme = "Numerisch" };
        args.Fingerprint = Plan(args).Fingerprint;

        TerminalRenumberData data = Execute(args);

        Assert.Null(Assert.Single(_renumberer.Renumbers).Strips);
        Assert.Equal((Other + ":?1", Other + ":2"), (Assert.Single(data.Changed).From, data.Changed[0].To));
        Assert.Equal((Other + ":?2", 1), (Assert.Single(data.StillUnnumbered), data.StillUnnumberedCount));
    }

    [Fact]
    public void Execute_OnlyUnnumbered_WhenEplanAlsoRenamesNumberedTerminals_RollsBackAndListsThem()
    {
        // The live run: given every terminal with "only number marked ones", EPLAN also reordered +A1-XD1 (A24).
        var args = new TerminalRenumberArgs { Project = "Line1", Scheme = "Numerisch" };
        args.Fingerprint = Plan(args).Fingerprint;
        _renumberer.Overreach = true;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack, "4"), (error.Reason, error.Execution, error.Metadata!["unplannedCount"]));
        Assert.Equal(
            [Strip + ":2 -> " + Strip + ":3", Strip + ":3 -> " + Strip + ":5", Strip + ":4 -> " + Strip + ":2", Strip + ":5 -> " + Strip + ":4"],
            error.Metadata["unplanned"].Split('\n'));
        Assert.StartsWith("EPLAN also renamed 4 terminals that were not to be numbered", error.Message, StringComparison.Ordinal);
        Assert.Contains("Rollback", _transactions.Calls);
    }

    [Fact]
    public void Execute_OneStrip_WhenEplanAlsoRenamesAnotherStrip_RollsBack()
    {
        var args = new TerminalRenumberArgs { Project = "Line1", Strips = [Other], Scheme = "Numerisch", OnlyUnnumbered = false };
        args.Fingerprint = Plan(args).Fingerprint;
        _renumberer.Overreach = true;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.All(error.Metadata!["unplanned"].Split('\n'), line => Assert.StartsWith(Strip + ":", line, StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_ASchemeEplanRefuses_RollsBackWithTheSchemesEplanNamed()
    {
        var args = new TerminalRenumberArgs { Project = "Line1", Strips = [Strip], Scheme = "Numerisch", OnlyUnnumbered = false };
        args.Fingerprint = Plan(args).Fingerprint;
        _renumberer.Refuse = true; // offered, but EPLAN refuses it while numbering

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal(5, error.Metadata!["candidates"].Split('\n').Length);
        Assert.Contains("Rollback", _transactions.Calls);
    }

    [Fact]
    public void Execute_WhenTheScopeChanged_IsStaleAndNothingIsBackedUp()
    {
        var args = new TerminalRenumberArgs { Project = "Line1", Strips = [Strip], Scheme = "Numerisch", OnlyUnnumbered = false };
        args.Fingerprint = Plan(args).Fingerprint;
        _renumberer.Strips[Strip][0].Name = Strip + ":9";

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_backup.Copies);
    }

    [Fact]
    public void Execute_WhenTheSchemeIsGoneOrTheBackupFails_NothingIsNumbered()
    {
        var args = new TerminalRenumberArgs { Project = "Line1", Strips = [Strip], Scheme = "Numerisch", OnlyUnnumbered = false };
        args.Fingerprint = Plan(args).Fingerprint;
        _renumberer.SchemeNames.Remove("Numerisch");
        BridgeError gone = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
        _renumberer.SchemeNames.Add("Numerisch");
        _backup.Failure = new IOException("disk full");

        BridgeError failed = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (gone.Reason, gone.Execution));
        Assert.Equal((ErrorReason.BackupFailed, ExecutionState.NotStarted), (failed.Reason, failed.Execution));
        Assert.Empty(_renumberer.Renumbers);
    }

    [Fact]
    public void Execute_WhenEplanGivesNewIdentifiers_SaysSoInsteadOfGuessing()
    {
        var args = new TerminalRenumberArgs { Project = "Line1", Strips = [Strip], Scheme = "Numerisch", OnlyUnnumbered = false };
        args.Fingerprint = Plan(args).Fingerprint;
        _renumberer.NewIds = true;

        TerminalRenumberData data = Execute(args);

        Assert.Empty(data.Changed);
        Assert.StartsWith("EPLAN gave 5 terminals new identifiers", data.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Changes_AreByTerminal_EachPairOnce_AndCountTerminalsWithoutAMatch()
    {
        PlacedFunction[] before = [Terminal("-X1:2", "1"), Terminal("-X1:2", "2"), Terminal("-X1:3", "3"), Terminal("-X1:?1", "4"), Terminal("-X1:9", "5")];
        PlacedFunction[] after = [Terminal("-X1:3", "1"), Terminal("-X1:3", "2"), Terminal("-X1:2", "3"), Terminal("-X1:9", "5")];

        (List<RenamedObject> changed, int unmatched) = TerminalRenumberRequest.Changes(before, after);

        Assert.Equal([("-X1:2", "-X1:3"), ("-X1:3", "-X1:2")], changed.Select(c => (c.From, c.To)));
        Assert.Equal(1, unmatched);
    }

    [Fact]
    public void Offered_DropsTheInternalSchemeAndRepeats()
    {
        Assert.Equal(["Standard", "Numerisch"], SchemeChoice.Offered(["Standard", SchemeChoice.Internal, "", "Numerisch", "Standard"]));
        Assert.Equal("Ask the engineer which scheme to use.", SchemeChoice.Refused("X", "no list", "cable", new InvalidOperationException()).Message.Split(". ")[^1]);
    }

    private static PlacedFunction Terminal(string name, string id) => new() { Name = name, IsMain = true, Id = id };

    private WritePlan Plan(TerminalRenumberArgs args) => (WritePlan)new TerminalRenumberPlanOperation(new FakeCatalog(_project), _renumberer).Execute(args)!;

    private TerminalRenumberData Execute(TerminalRenumberArgs args)
    {
        args.Backup = new BackupTarget { Path = Path.Combine(_folder, "Line1.elk"), MaxSeconds = 150 };
        return (TerminalRenumberData)new TerminalRenumberExecuteOperation(new FakeCatalog(_project), _pages, _renumberer, _backup, _transactions).Execute(args)!;
    }
}
