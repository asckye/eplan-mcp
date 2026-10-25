using System.Security.Cryptography;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Medium")]
public sealed class PlanStepExecuteFileTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-plan-step-").FullName;
    public void Dispose() => Directory.Delete(_root, recursive: true);
    [Theory]
    [InlineData("page.delete")]
    [InlineData("page.renumber")]
    [InlineData("device.renumber")]
    public void Destructive_PerStepBackupPrecedesExactlyOneTransaction(string op)
    {
        using var h = new PlanStepHarness();
        PlanStepExecuteArgs args = h.Step(op);
        args.Backup = Target("step");
        // An initial backup is additional and cannot be substituted for this step's backup.
        ProjectBackups.Make(h.Backup, h.Pages, h.Catalog.ListOpenProjects()[0], Target("initial"));
        h.Transactions.Snapshot = () =>
        {
            Assert.Equal(2, h.Backup.Copies.Count);
            Assert.Equal(2, h.Backup.Counted.Count);
            Assert.True(File.Exists(args.Backup.Path));
            return () =>
            {
            };
        };
        h.Run(args);
        Assert.Equal(["Begin AI: plan 01234567 2/3 " + op, "Commit", "CloseUndo", "Dispose"], h.Transactions.Calls);
        Assert.Equal(2, h.Backup.Copies.Count);
        Assert.Equal(OutcomeStatus.Committed, h.Outcome());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Destructive_MissingOrFailedBackup_NotStartedWithoutTransaction(bool fail)
    {
        using var h = new PlanStepHarness();
        PlanStepExecuteArgs args = h.Step("page.delete");
        if (fail)
        {
            args.Backup = Target("failed");
            h.Backup.Failure = new IOException("Copy failed");
        }

        BridgeError error = Assert.Throws<BridgeErrorException>(() => h.Run(args)).Error;
        Assert.Equal((ErrorReason.BackupFailed, ExecutionState.NotStarted, OutcomeStatus.NotStarted), (error.Reason, error.Execution, h.Outcome()));
        Assert.Empty(h.Transactions.Calls);
        Assert.Empty(h.Remover.Removed);
    }

    [Fact]
    public void Destructive_ApprovedNumberingDetailsChanged_IsNotStartedBeforeBackup()
    {
        using var h = new PlanStepHarness();
        PlanStepExecuteArgs args = h.Step("page.renumber");
        args.Backup = Target("unused");
        args.PageRenumber!.Start++;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => h.Run(args)).Error;
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(h.Transactions.Calls);
        Assert.Empty(h.Backup.Copies);
    }

    [Fact]
    public void Macro_FileChangedAfterCompileWithSameSizeAndTimestamp_IsRefusedBySha256()
    {
        using var h = MacroHarness();
        PlanStepExecuteArgs args = MacroStep(h);
        string path = Path.Combine(_root, "test.ema");
        DateTime before = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, "other");
        File.SetLastWriteTimeUtc(path, before);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => h.Run(args)).Error;
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted, OutcomeStatus.NotStarted), (error.Reason, error.Execution, h.Outcome()));
        Assert.Empty(h.Transactions.Calls);
        Assert.Empty(h.Inserter.Inserts);
        using var writable = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.None);
    }

    [Fact]
    public void Macro_ReadHandlePreventsWriteAndReplacementUntilInsertionEnds_OneTransaction()
    {
        using var h = MacroHarness();
        PlanStepExecuteArgs args = MacroStep(h);
        args.ApprovedDetails = null; // P10 predicted macro uses its full compiled description instead of sampled details.
        string path = Path.Combine(_root, "test.ema");
        h.Inserter.Placed = _ =>
        {
            Assert.Throws<IOException>(() => File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite).Dispose());
            Assert.Throws<IOException>(() => File.Move(path, path + ".moved"));
            using var reader = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return [];
        };
        h.Run(args);
        Assert.Single(h.Inserter.Inserts);
        Assert.Equal(["Begin AI: plan 01234567 2/3 macro.insert", "Commit", "CloseUndo", "Dispose"], h.Transactions.Calls);
        Assert.Equal(OutcomeStatus.Committed, h.Outcome());
        using var writable = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.None);
    }

    [Theory]
    [InlineData("description")]
    [InlineData("occupiedPage")]
    [InlineData("variant")]
    [InlineData("numbering")]
    [InlineData("position")]
    public void Macro_ConstraintOrRestrictedInsertionChanged_IsNotStarted(string failure)
    {
        using var h = MacroHarness();
        PlanStepExecuteArgs args = MacroStep(h);
        switch (failure)
        {
            case "description":
                args.MacroConstraint!.Description.ObjectCount++;
                break;
            case "occupiedPage":
                h.Inserter.Pages[PlanStepHarness.Page].Objects.Add("28/17/9/0");
                break;
            case "variant":
                args.MacroInsert!.Variant = 1;
                break;
            case "numbering":
                args.MacroInsert!.Numbering = "number";
                break;
            case "position":
                args.MacroInsert!.X += 0.001; // Existing preview rounds positions to two decimal places.
                break;
        }

        BridgeError error = Assert.Throws<BridgeErrorException>(() => h.Run(args)).Error;
        Assert.Equal(ExecutionState.NotStarted, error.Execution);
        Assert.Empty(h.Transactions.Calls);
        Assert.Empty(h.Inserter.Inserts);
    }

    private PlanStepHarness MacroHarness()
    {
        File.WriteAllText(Path.Combine(_root, "test.ema"), "macro");
        var h = new PlanStepHarness(_root);
        h.Macros.Content.Complete = true;
        h.Macros.Content.FunctionCount = 0;
        h.Macros.Content.Representation = "MultiLine";
        h.Macros.Content.Variant = 0;
        return h;
    }

    private PlanStepExecuteArgs MacroStep(PlanStepHarness h)
    {
        PlanStepExecuteArgs args = h.Step("macro.insert");
        string hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(_root, "test.ema"))));
        args.MacroConstraint = new()
        {
            Description = PlanMacroInspectOperation.Describe(h.Macros.Content, hash),
            Arguments = System.Text.Json.JsonSerializer.Deserialize<MacroInsertArgs>(System.Text.Json.JsonSerializer.Serialize(args.MacroInsert))!,
        };
        return args;
    }

    private BackupTarget Target(string folder) => new()
    {
        Path = Path.Combine(Directory.CreateDirectory(Path.Combine(_root, folder)).FullName, "Line1.elk"),
        MaxSeconds = 30,
    };
}
