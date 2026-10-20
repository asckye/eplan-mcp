using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>macro.list over a temporary macro directory; the scan itself is covered by <see cref="MacroFilesTests"/>.</summary>
[Trait("Size", "Medium")]
public sealed class MacroListOperationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-macro-list-").FullName;

    public MacroListOperationTests()
    {
        foreach (string relative in new[] { @"EPLAN_Macro\Contactor_Feedback.ema", @"EPLAN_Macro\Motor_Start.ema", @"Siemens\3RT2_contactor.ema" })
        {
            string path = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Empty);
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Execute_FiltersByPathIgnoringCase_AndNamesEachMacro()
    {
        MacroListData data = Run(new MacroListArgs { NameContains = "CONTACTOR", Limit = 50 });

        Assert.Equal(
            [("EPLAN_Macro/Contactor_Feedback.ema", "Contactor_Feedback"), ("Siemens/3RT2_contactor.ema", "3RT2_contactor")],
            data.Items.Select(m => (m.Path, m.Name)));
        Assert.Equal(2, data.Total);
        Assert.False(data.Incomplete);
    }

    [Fact]
    public void Execute_MatchesFolderNames_AndABlankFilterMeansEveryMacro()
    {
        Assert.Equal(2, Run(new MacroListArgs { NameContains = "eplan_macro/", Limit = 50 }).Total);
        Assert.Equal(3, Run(new MacroListArgs { NameContains = "  ", Limit = 50 }).Total);
    }

    [Fact]
    public void Execute_SlicesInPathOrder()
    {
        MacroListData data = Run(new MacroListArgs { Offset = 1, Limit = 1 });

        Assert.Equal(["EPLAN_Macro/Motor_Start.ema"], data.Items.Select(m => m.Path));
        Assert.Equal(3, data.Total);
    }

    [Fact]
    public void Execute_AScanPastItsBudget_IsIncomplete()
    {
        var operation = new MacroListOperation(new FakeMacroReader(_root), TimeSpan.Zero);

        var data = (MacroListData)operation.Execute(new MacroListArgs { Limit = 50 })!;

        Assert.True(data.Incomplete);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("missing")]
    public void Execute_WithoutAMacroDirectory_FailsWithoutNamingAPath(string? root)
    {
        string? given = root is null ? null : Path.Combine(_root, root);
        var operation = new MacroListOperation(new FakeMacroReader(given));

        BridgeError error = Assert.Throws<BridgeErrorException>(() => operation.Execute(new MacroListArgs { Limit = 50 })).Error;

        Assert.Equal(ErrorReason.EplanOperationFailed, error.Reason);
        Assert.DoesNotContain(_root, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private MacroListData Run(MacroListArgs args) => (MacroListData)new MacroListOperation(new FakeMacroReader(_root)).Execute(args)!;

    private sealed class FakeMacroReader(string? root) : IMacroReader
    {
        public string? MacroRoot() => root;
    }
}
