using EplanMcp.Evals.Run;

namespace EplanMcp.Evals.Tests.Run;

[Trait("Size", "Small")]
public sealed class RunOptionsTests
{
    [Fact]
    public void Parse_NoOptions_RunsTheDevSetOnceWithTheDesignsModel()
    {
        RunOptions? options = RunOptions.Parse([], out string? error);

        Assert.Equal(new RunOptions(null, null, null, 1, "claude-sonnet-5-5", null, null), options);
        Assert.Equal("dev", options!.SetToRun);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(new[] { "--task", "part-count" }, "all")]
    [InlineData(new[] { "--task", "part-count", "--set", "dev" }, "dev")]
    [InlineData(new[] { "--set", "holdout" }, "holdout")]
    public void SetToRun_ATaskAloneIsLookedForInBothSets(string[] args, string set)
    {
        Assert.Equal(set, RunOptions.Parse(args, out _)!.SetToRun);
    }

    [Fact]
    public void Parse_EveryOption_IsRead()
    {
        RunOptions? options = RunOptions.Parse(
            ["--tasks", "t", "--set", "all", "--task", "page-count", "--repeat", "3", "--model", "claude-opus-5-5", "--claude", "c.exe", "--eplanmcp", "e.exe"],
            out _);

        Assert.Equal(new RunOptions("t", "all", "page-count", 3, "claude-opus-5-5", "c.exe", "e.exe"), options);
    }

    [Theory]
    [InlineData("--set", "nightly", "--set is dev, holdout or all, not nightly")]
    [InlineData("--repeat", "0", "--repeat is a whole number from 1 to 10, not 0")]
    [InlineData("--repeat", "11", "--repeat is a whole number from 1 to 10, not 11")]
    [InlineData("--repeat", "-1", "--repeat is a whole number from 1 to 10, not -1")]
    [InlineData("--sets", "dev", "unknown option --sets")]
    public void Parse_AWrongOption_SaysWhat(string option, string value, string expected)
    {
        Assert.Null(RunOptions.Parse([option, value], out string? error));
        Assert.Equal(expected, error);
    }

    [Fact]
    public void Parse_AnOptionWithoutValue_IsRefused()
    {
        Assert.Null(RunOptions.Parse(["--set", "dev", "--task"], out string? error));
        Assert.Equal("--task needs a value", error);
    }
}
