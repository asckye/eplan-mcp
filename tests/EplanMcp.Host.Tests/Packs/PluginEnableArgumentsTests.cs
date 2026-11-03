using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Packs;

[Trait("Size", "Small")]
public sealed class PluginEnableArgumentsTests
{
    [Theory]
    [InlineData("", "1.0.0")]
    [InlineData(" ", "1.0.0")]
    [InlineData("../plugin", "1.0.0")]
    [InlineData("C:\\plugin", "1.0.0")]
    [InlineData("plugin\n", "1.0.0")]
    [InlineData("PLUGIN", "1.0.0")]
    [InlineData("plugin", "")]
    [InlineData("plugin", "latest")]
    [InlineData("plugin", "^1.0.0")]
    [InlineData("plugin", "1.0.0\n")]
    public async Task InvalidArguments_AreRejectedBeforeReadingState(string plugin, string version)
    {
        Assert.True((await PluginEnableTools.SetAsync(null!, plugin, version, true,
            cancellationToken: TestContext.Current.CancellationToken)).IsError);
    }

    [Theory]
    [InlineData("enable")]
    [InlineData("disable")]
    public void ApprovalText_NamesExactVersionAndGlobalScope(string action)
    {
        var plan = new WritePlan
        {
            Changes = [new PlannedChange { Action = action, Kind = "plugin", Name = "iec-base@1.0.0", Property = "global" }],
        };
        var question = new ApprovalQuestion("test", PluginEnableTools.Tool, "hash", plan, [], DateTimeOffset.MaxValue)
        { Tier = ToolTier.Write, Eplan = null };
        ApprovalAskArgs text = ApprovalTexts.For(question, CultureInfo.GetCultureInfo("en-US"));
        Assert.Equal(action + " plugin iec-base@1.0.0 globally", Assert.Single(text.Changes));
        Assert.Contains("cannot undo", text.Note, StringComparison.Ordinal);
    }
}
