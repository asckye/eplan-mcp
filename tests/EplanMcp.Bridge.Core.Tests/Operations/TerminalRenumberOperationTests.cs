using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// terminal.renumber.plan and terminal.renumber.execute against a fake that numbers the way spike S21 found EPLAN
/// numbers the strip +A1-XD1 of the sample project (api-notes A24): the scheme check, what counts as unnumbered, the
/// scope, the change list and the read-back by terminal.
/// </summary>
[Trait("Size", "Small")]
public sealed class TerminalRenumberOperationTests
{
    private const string Strip = "+A1-XD1";
    private const string Other = "+A1-XD4";

    private static readonly OpenProject _project = new() { Name = "Line1", Path = @"D:\Scratch\Line1" };

    private readonly FakeTerminalRenumberer _renumberer = new();

    public TerminalRenumberOperationTests()
    {
        _renumberer.Strips[Strip] = [.. Enumerable.Range(1, 5).Select(d => Terminal(Strip + ":" + d.ToString(System.Globalization.CultureInfo.InvariantCulture), "x" + d.ToString(System.Globalization.CultureInfo.InvariantCulture)))];
        _renumberer.Strips[Other] = [Terminal(Other + ":1", "o1"), Terminal(Other + ":?1", "o2"), Terminal(Other + ":?2", "o3")];

        // S21: four terminals of +A1-XD1 took each other's numbers in the strip's order.
        _renumberer.Numbers[Strip + ":2"] = Strip + ":3";
        _renumberer.Numbers[Strip + ":3"] = Strip + ":5";
        _renumberer.Numbers[Strip + ":4"] = Strip + ":2";
        _renumberer.Numbers[Strip + ":5"] = Strip + ":4";
        _renumberer.Numbers[Other + ":?1"] = Other + ":2";
    }

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
}
