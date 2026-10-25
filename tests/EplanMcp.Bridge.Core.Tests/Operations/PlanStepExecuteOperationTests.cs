using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using EplanMcp.Tests;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlanStepExecuteOperationTests
{
    [Theory]
    [InlineData("added")]
    [InlineData("replaced")]
    [InlineData("moved")]
    [InlineData("removed")]
    public void Step_LocalRenameChangedClosureOutsideReadPages_IsStaleBeforeTransaction(string change)
    {
        using var h = new PlanStepHarness();
        PlanStepExecuteArgs args = h.Step("device.update");
        args.Guard = h.GuardReader.Args(all: true);
        if (change != "removed")
        {
            h.GuardReader.Names.Add(new()
            {
                Name = "+A1-K1",
                Type = 17,
                Oid = change == "replaced" ? 99 : 2,
                Page = change == "moved" ? "+A9/9" : PlanStepHarness.Page,
                IsMain = true,
            });
        }
        if (change == "added")
        {
            h.GuardReader.Names.Add(new() { Name = "+A1-K1", Type = 17, Oid = 99, Page = null });
        }

        BridgeError failure = Assert.Throws<BridgeErrorException>(() => h.Run(args)).Error;
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (failure.Reason, failure.Execution));
        Assert.Equal("closure", (string?)Assert.Single(JArray.Parse(failure.Metadata!["conflicts"]))["reason"]);
        Assert.Equal(1, h.GuardReader.NameReads);
        Assert.Empty(h.Transactions.Calls);
        Assert.Equal(OutcomeStatus.NotStarted, h.Outcome());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Step_LocalRenameStableClosure_UsesApprovedFullNameAndOneEnumerationAndCommits(bool shortTag)
    {
        using var h = new PlanStepHarness();
        PlanStepExecuteArgs args = h.Step("device.update");
        if (shortTag)
        {
            h.Devices.Functions[0].VisibleName = "-K1";
            args.DeviceUpdate!.Tag = "-K1";
        }
        args.Guard = h.GuardReader.Args(all: true);
        h.GuardReader.Names.Add(new() { Name = "+A1-K1", Type = 17, Oid = 2, Page = PlanStepHarness.Page, IsMain = true });
        h.Run(args);
        Assert.Equal(1, h.GuardReader.NameReads);
        Assert.Equal(OutcomeStatus.Committed, h.Outcome());
    }

    [Theory]
    [InlineData("page.create")]
    [InlineData("page.update")]
    [InlineData("device.update")]
    public void Step_EffectiveWrite_OneDispatchOneNamedTransactionAndOneOutcome(string op)
    {
        using var h = new PlanStepHarness();
        PlanStepExecuteArgs args = h.Step(op);
        h.Run(args);
        Assert.Equal(["Begin AI: plan 01234567 2/3 " + op, "Commit", "CloseUndo", "Dispose"], h.Transactions.Calls);
        Assert.Equal((1, 1, OutcomeStatus.Committed), (h.Dispatcher.Calls, h.GuardReader.Reads, h.Outcome()));
        Assert.Throws<BridgeErrorException>(() => h.Run(args));
        Assert.Equal(1, h.Dispatcher.Calls);
        Assert.DoesNotContain(OperationNames.PageCreateExecute, h.Router.SupportedOperations);
    }

    [Theory]
    [InlineData("guard", ErrorReason.PlanStale)]
    [InlineData("timeout", ErrorReason.Timeout)]
    [InlineData("changes", ErrorReason.PlanStale)]
    [InlineData("fingerprint", ErrorReason.PlanStale)]
    [InlineData("planning", ErrorReason.InvalidArgument)]
    [InlineData("multiple", ErrorReason.InvalidArgument)]
    [InlineData("project", ErrorReason.InvalidArgument)]
    [InlineData("nestedFingerprint", ErrorReason.InvalidArgument)]
    [InlineData("bounds", ErrorReason.InvalidArgument)]
    public void Step_PreTransactionFailure_NotStartedAndNoUndoStep(string failure, string reason)
    {
        using var h = new PlanStepHarness();
        PlanStepExecuteArgs args = h.Step("page.create");
        switch (failure)
        {
            case "guard":
                h.GuardReader.Content[1].Clear();
                // Keep the page readable while changing its member set.
                h.GuardReader.Content[1].Add(new() { Kind = "page", Identifier = "28/4/1/0", Oid = 1, Name = PlanStepHarness.Page });
                break;
            case "timeout":
                h.GuardReader.PerRead = 10_000;
                break;
            case "changes":
                args.ApprovedChanges[0].NewValue = "Different";
                break;
            case "fingerprint":
                args.Fingerprint = "different";
                break;
            case "planning":
                args.PageCreate!.Pages[0].PageType = "Unknown";
                break;
            case "multiple":
                args.PageUpdate = new();
                break;
            case "project":
                args.PageCreate!.Project = "Other";
                break;
            case "nestedFingerprint":
                args.PageCreate!.Fingerprint = args.Fingerprint;
                break;
            case "bounds":
                args.Total = 21;
                break;
        }

        BridgeError error = Assert.Throws<BridgeErrorException>(() => h.Run(args)).Error;
        Assert.Equal((reason, ExecutionState.NotStarted, OutcomeStatus.NotStarted), (error.Reason, error.Execution, h.Outcome()));
        Assert.Empty(h.Transactions.Calls);
        Assert.Empty(h.Writer.Creates);
        Assert.Empty(h.Backup.Copies);
    }

    [Fact]
    public void Step_UnexpectedPlanningFailure_IsStampedBeforeRouterDefault()
    {
        using var h = new PlanStepHarness();
        PlanStepExecuteArgs args = h.Step("page.create");
        args.PageCreate!.Pages = null!;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => h.Run(args)).Error;
        Assert.Equal(ExecutionState.NotStarted, error.Execution);
        Assert.Equal(OutcomeStatus.NotStarted, h.Outcome());
        Assert.Empty(h.Transactions.Calls);
    }

    [Theory]
    [InlineData("approval", true)]
    [InlineData("operation", "module.insert")]
    [InlineData("placeholderApply", true)]
    public void Parse_UnknownFieldsIncludingApprovalAndUnsupportedOperations_AreRefused(string field, object value)
    {
        using var h = new PlanStepHarness();
        var json = (JObject)BridgeJson.FromObject(h.Step("page.create"));
        json[field] = JToken.FromObject(value);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => h.Router.Invoke(OperationNames.PlanStepExecute, json, "invalid")).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Equal(0, h.Dispatcher.Calls);
    }

    [Fact]
    public void Step_ExecuteFails_RollsBackExactlyOneTransaction()
    {
        using var h = new PlanStepHarness();
        PlanStepExecuteArgs args = h.Step("page.create");
        h.Writer.ChangeDescription = _ => "Unexpected";
        BridgeError error = Assert.Throws<BridgeErrorException>(() => h.Run(args)).Error;
        Assert.Equal(ExecutionState.RolledBack, error.Execution);
        Assert.Equal(["Begin AI: plan 01234567 2/3 page.create", "CloseUndo", "Rollback", "Dispose"], h.Transactions.Calls);
        Assert.Single(h.Pages.Pages);
        Assert.Equal(OutcomeStatus.RolledBack, h.Outcome());
    }

    [Fact]
    public void Step_CommitFailure_PreservesUnknownOutcome()
    {
        using var h = new PlanStepHarness();
        h.Transactions.FailAt = "Commit";
        BridgeError error = Assert.Throws<BridgeErrorException>(() => h.Run(h.Step("page.create"))).Error;
        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown, OutcomeStatus.Unknown), (error.Reason, error.Execution, h.Outcome()));
    }

    [Fact]
    public void SingleStep_AfterPlanStep_StillUsesOriginalFactoryAndDescription()
    {
        using var h = new PlanStepHarness();
        h.Run(h.Step("page.update"));
        var args = new PageCreateArgs
        {
            Project = PlanGuardReader.Project,
            Pages = [new()
            {
                Name = "+A1/3"
            }
            ]
        };
        args.Fingerprint = ((WritePlan)new PageCreatePlanOperation(h.Catalog, h.Pages, h.Writer).Execute(args)!).Fingerprint;
        new PageCreateExecuteOperation(h.Catalog, h.Pages, h.Writer, h.Transactions).Execute(args);
        Assert.Equal(["Begin AI: plan 01234567 2/3 page.update", "Begin AI: eplan_page_create"], h.Transactions.Calls.Where(c => c.StartsWith("Begin", StringComparison.Ordinal)));
    }
}
