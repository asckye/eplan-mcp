using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

public sealed partial class PlanLocalEvidenceTests
{
    [Fact]
    public async Task LP8_LargeWrite_OutsidePageCreatedBetweenSteps_StopsWithExternalChange()
    {
        Assert.SkipWhen(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("EPLANMCP_LARGE_FIXTURE")),
            "Run this test on the LargeWrite scratch copy.");
        await RunAsync(await eplan.WriteProjectAsync(), null, "names");
    }

    [Fact]
    public async Task LP8_LargeWrite_FarDescriptionChangedBetweenSteps_CompletesWithUnobservedCoverage()
    {
        Assert.SkipWhen(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("EPLANMCP_LARGE_FIXTURE")),
            "Run this test on the LargeWrite scratch copy.");
        await RunAsync(await eplan.WriteProjectAsync(), null, "description");
    }

    private static async Task CreateBoundaryPageAsync(BridgeClient bridge, string project, string page)
    {
        var args = new PageCreateArgs
        {
            Project = project,
            Language = "en_US",
            Pages = [new() { Name = page, PageType = "Circuit", Description = "LP8 outside the plan" }],
        };
        args.Fingerprint = (await bridge.InvokeAsync<PageCreateArgs, WritePlan>(OperationNames.PageCreatePlan, args, Token)).Fingerprint;
        await bridge.InvokeWriteAsync<PageCreateArgs, JsonElement>(OperationNames.PageCreateExecute, args, Guid.NewGuid().ToString("N"),
            TimeSpan.FromSeconds(60), null, Token);
    }

    private static async Task UpdateBoundaryPageAsync(BridgeClient bridge, string project, string page)
    {
        var args = new PageUpdateArgs
        {
            Project = project,
            Language = "en_US",
            Pages = [new() { Name = page, Description = "LP8 far description changed between steps" }],
        };
        args.Fingerprint = (await bridge.InvokeAsync<PageUpdateArgs, WritePlan>(OperationNames.PageUpdatePlan, args, Token)).Fingerprint;
        await bridge.InvokeWriteAsync<PageUpdateArgs, JsonElement>(OperationNames.PageUpdateExecute, args, Guid.NewGuid().ToString("N"),
            TimeSpan.FromSeconds(60), null, Token);
    }
}
