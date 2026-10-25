using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

/// <summary>P17/U1: invoke the Bridge directly, without a single-step approval channel or prompt.</summary>
[Trait("Size", "Large")]
public sealed class PlanStepExecuteTests(LiveEplan eplan)
{
    [Theory]
    [InlineData("page.create")]
    [InlineData("page.update")]
    [InlineData("page.delete")]
    [InlineData("page.renumber")]
    [InlineData("device.update")]
    [InlineData("device.renumber")]
    [InlineData("macro.insert")]
    public async Task Step_SevenOperations_CommitThenOneUndoRestoresExactlyThatStep(string op)
    {
        await using PlanStepLiveSession run = await PlanStepLiveSession.StartAsync(eplan);
        string[] pages = await run.CreatePagesAsync(2);
        string target = pages[1];
        object args;
        switch (op)
        {
            case "page.create":
                args = new PageCreateArgs
                {
                    Project = run.Project,
                    Language = "en_US",
                    Pages = [new()
                    {
                        Name = run.NextPage,
                        Description = "P17 two pages, one undo"
                    }, new()
                    {
                        Name = run.NextPage
                    }
                    ]
                };
                break;
            case "page.update":
                args = new PageUpdateArgs
                {
                    Project = run.Project,
                    Language = "en_US",
                    Pages = [new()
                    {
                        Name = target,
                        Description = "P17 changed"
                    }
                    ]
                };
                break;
            case "page.delete":
                args = new PageDeleteArgs
                {
                    Project = run.Project,
                    Pages = [target]
                };
                break;
            case "page.renumber":
                args = new PageRenumberArgs
                {
                    Project = run.Project,
                    Pages = [target],
                    Start = run.NextNumber
                };
                break;
            case "device.update":
            case "device.renumber":
                JsonElement inserted = await run.InsertFixtureMacroAsync(target);
                string tag = inserted.GetProperty("devices").EnumerateArray().Select(d => d.GetProperty("tag").GetString()!).First(t => t.Contains('-', StringComparison.Ordinal) && !t.Contains('?', StringComparison.Ordinal) && !t.Contains(':', StringComparison.Ordinal));
                if (op == "device.renumber")
                {
                    await run.RenameAsync(tag, tag.Insert(tag.LastIndexOf('-') + 1, "?") + "99");
                }

                args = op == "device.update" ? new DeviceUpdateArgs
                {
                    Project = run.Project,
                    Tag = tag,
                    NewTag = tag + "99"
                }

                : new DeviceRenumberArgs
                {
                    Project = run.Project,
                    Pages = [target],
                    OnlyUnnumbered = true,
                    Start = run.NextNumber
                };
                break;
            default:
                args = new MacroInsertArgs
                {
                    Project = run.Project,
                    Page = target,
                    Macro = await run.FindSupportedMacroAsync(target),
                    X = 40,
                    Y = 280,
                    Numbering = "keep"
                };
                break;
        }

        PlanStepExecuteArgs step = await run.PrepareAsync(op, args, pages);
        string before = await run.StateAsync();
        string[] openBefore = await run.OpenPathsAsync();
        string request = Guid.NewGuid().ToString("N");
        WriteResult<JsonElement> committed = await run.ExecuteAsync(step, request);
        Assert.False(committed.Recovered);
        Assert.Equal(OutcomeStatus.Committed, await run.OutcomeAsync(request));
        string after = await run.StateAsync();
        if (op == "device.renumber" && committed.Data.GetProperty("changedCount").GetInt32() == 0)
        {
            Assert.Equal(before, after);
            Assert.Skip("EPLAN left all fixture tags unchanged (S35/A41); no undo action is safe for an ineffective step.");
        }

        Assert.NotEqual(before, after);
        Assert.Equal(openBefore, await run.OpenPathsAsync());
        if (step.Backup is { } backup)
        {
            Assert.True(File.Exists(backup.Path));
            Assert.True(Directory.Exists(Path.ChangeExtension(backup.Path, ".edb")));
        }

        await run.UndoAsync();
        Assert.Equal(before, await run.StateAsync());
        Assert.Equal(OutcomeStatus.Committed, await run.OutcomeAsync(request));
        TestContext.Current.TestOutputHelper!.WriteLine($"{op}: committed request {request}; one XEsUndoAction restored the complete standard snapshot and earlier setup pages.");
    }

    [Fact]
    public async Task Step_GuardConflict_NoUndoStepAndPreviousWriteUndoesOnce()
    {
        await using PlanStepLiveSession run = await PlanStepLiveSession.StartAsync(eplan);
        string page = (await run.CreatePagesAsync(1))[0];
        string before = await run.StateAsync();
        var args = new PageUpdateArgs
        {
            Project = run.Project,
            Language = "en_US",
            Pages = [new()
            {
                Name = page,
                Description = "Approved"
            }
            ]
        };
        PlanStepExecuteArgs stale = await run.PrepareAsync("page.update", args, [page]);
        await run.UpdateAsync(page, "External change after compilation");
        string changed = await run.StateAsync();
        string request = Guid.NewGuid().ToString("N");
        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => run.ExecuteAsync(stale, request));
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Error.Reason, error.Error.Execution));
        Assert.Equal(OutcomeStatus.NotStarted, await run.OutcomeAsync(request));
        Assert.Equal(changed, await run.StateAsync());
        await run.UndoAsync();
        Assert.Equal(before, await run.StateAsync());
    }

    [Fact]
    public async Task Step_TransactionFailure_RollsBackAndLeavesNoUndoStep()
    {
        await using PlanStepLiveSession run = await PlanStepLiveSession.StartAsync(eplan);
        string page = (await run.CreatePagesAsync(1))[0];
        Assert.Contains(DestructiveHost.Prefix + "1", (await run.SnapshotAsync()).Pages.Select(p => p.Name));
        string before = await run.StateAsync();
        var args = new PageRenumberArgs
        {
            Project = run.Project,
            Pages = [page],
            Start = 1
        };
        PlanStepExecuteArgs step = await run.PrepareAsync("page.renumber", args, [page]);
        string request = Guid.NewGuid().ToString("N");
        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => run.ExecuteAsync(step, request));
        Assert.Equal(ExecutionState.RolledBack, error.Error.Execution);
        Assert.Equal(OutcomeStatus.RolledBack, await run.OutcomeAsync(request));
        Assert.Equal(before, await run.StateAsync());
        await run.UndoAsync();
        Assert.DoesNotContain(page, (await run.SnapshotAsync()).Pages.Select(p => p.Name));
    }
}
