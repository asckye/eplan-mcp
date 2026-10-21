using EplanMcp.Bridge.Core.Hosting;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Hosting;

[Trait("Size", "Small")]
public sealed class OfflineHostOptionsTests
{
    private const string Bin = @"C:\Program Files\EPLAN\Electric P8\2026.0.3\Bin";
    private const string Project = @"C:\Users\u\EplanMcp\scratch\offline\EPLAN_Sample_Project.elk";

    [Fact]
    public void Parse_TheRequiredOptionsOnly_UsesTheAddInsPrefixAndNoIdleExit()
    {
        OfflineHostOptions options = Parse("--variant-bin", Bin, "--project", Project)!;

        Assert.Equal((Bin, Project, BridgeProtocol.PipePrefix, (TimeSpan?)null), (options.VariantBin, options.Project, options.PipePrefix, options.IdleExit));
    }

    [Fact]
    public void Parse_EveryOption_InAnyOrder()
    {
        OfflineHostOptions options = Parse("--idle-exit", "30", "--pipe-prefix", "EplanMcpCi.a1-b_2.", "--project", Project, "--variant-bin", Bin)!;

        Assert.Equal((Bin, Project, "EplanMcpCi.a1-b_2.", (TimeSpan?)TimeSpan.FromSeconds(30)), (options.VariantBin, options.Project, options.PipePrefix, options.IdleExit));
    }

    [Theory]
    [InlineData("--project", Project)]
    [InlineData("--variant-bin", Bin)]
    public void Parse_WithoutARequiredOption_SaysWhichOne(string name, string value)
    {
        Assert.Null(OfflineHostOptions.Parse([name, value], out string? problem));
        Assert.Contains(name == "--project" ? "--variant-bin" : "--project", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--idle-exit", "0")]
    [InlineData("--idle-exit", "-5")]
    [InlineData("--idle-exit", "1.5")]
    [InlineData("--idle-exit", "86401")]
    [InlineData("--idle-exit", "ten")]
    [InlineData("--pipe-prefix", @"EplanMcp\Bridge.")]
    [InlineData("--pipe-prefix", "Eplan Mcp.")]
    [InlineData("--pipe-prefix", "EplanMcp.Bridge.EplanMcp.Bridge.EplanMcp.Bridge.EplanMcp.Bridge.EplanMcp.")]
    public void Parse_AnInvalidValue_IsRefusedWithTheOption(string name, string value)
    {
        Assert.Null(OfflineHostOptions.Parse(["--variant-bin", Bin, "--project", Project, name, value], out string? problem));
        Assert.StartsWith(name, problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--project", @"C:\scratch\Line1")]
    [InlineData("--project", @"C:\scratch\Line1.edb")]
    [InlineData("--project", @"scratch\Line1.elk")]
    [InlineData("--project", "C:\\scratch\\Line|1.elk")]
    [InlineData("--variant-bin", @"Electric P8\2026.0.3\Bin")]
    public void Parse_APathThatIsNotFullOrNotAnElkFile_IsRefused(string name, string value)
    {
        string[] args = name == "--project" ? ["--variant-bin", Bin, name, value] : [name, value, "--project", Project];

        Assert.Null(OfflineHostOptions.Parse(args, out string? problem));
        Assert.StartsWith(name, problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--project")]
    [InlineData("--project", "--idle-exit", "5")]
    [InlineData("--project", "")]
    public void Parse_AnOptionWithoutAValue_IsRefused(params string[] tail)
    {
        Assert.Null(OfflineHostOptions.Parse(["--variant-bin", Bin, .. tail], out string? problem));
        Assert.Equal("--project needs a value.", problem);
    }

    [Fact]
    public void Parse_AnOptionTwice_IsRefused()
    {
        Assert.Null(OfflineHostOptions.Parse(["--variant-bin", Bin, "--project", Project, "--project", Project], out string? problem));
        Assert.Equal("--project is given twice.", problem);
    }

    [Theory]
    [InlineData("--pipe")]
    [InlineData("serve")]
    [InlineData("--Project")]
    public void Parse_AnUnknownOption_IsRefused(string name)
    {
        Assert.Null(OfflineHostOptions.Parse(["--variant-bin", Bin, "--project", Project, name, "x"], out string? problem));
        Assert.Equal($"Unknown option '{name}'.", problem);
    }

    [Fact]
    public void Parse_NoArguments_AsksForTheVariantBin()
    {
        Assert.Null(OfflineHostOptions.Parse([], out string? problem));
        Assert.Equal("--variant-bin is required.", problem);
    }

    private static OfflineHostOptions? Parse(params string[] args)
    {
        OfflineHostOptions? options = OfflineHostOptions.Parse(args, out string? problem);
        Assert.Null(problem);
        return options;
    }
}
