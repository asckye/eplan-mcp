using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>The call's copied system messages include backup diagnostics without changing the transaction outcome.</summary>
[Trait("Size", "Small")]
public sealed class CableGenerateMessageTests
{
    private readonly FakeCatalog _catalog = new(new OpenProject { Name = "Line1", Path = @"D:\Scratch\Line1" });
    private readonly FakeTransactions _transactions = new();
    private readonly Messages _messages = new();
    private readonly Generator _generator = new();
    private Action? _onBackup;

    [Fact]
    public void Success_OnlyNewMessages_MapsLevelsAndLimitsToTwenty()
    {
        _messages.Records.Add(new() { Text = "old", Level = 4 });
        _generator.Run = () =>
        {
            _messages.Records.Add(new() { Text = "trace", Level = 0 });
            _messages.Records.AddRange(Enumerable.Range(0, 25).Select(i => new SystemMessageRecord { Text = "new", Index = i % 2 == 0 ? "S000000" : string.Empty, Level = i % 6 + 1, Occurrences = 2 }));
        };

        CableGenerateData result = Execute();

        Assert.Equal(25, result.EplanMessageCount);
        Assert.Equal(20, result.EplanMessages.Count);
        Assert.All(result.EplanMessages, m => { Assert.Equal("new", m.Text); Assert.Null(m.Number); Assert.Equal(2, m.Occurrences); });
        Assert.Equal(["message", "warning", "warning", "error", "fatal", "message"], result.EplanMessages.Take(6).Select(m => m.Level));
        Assert.Equal(2, _messages.Reads);
    }

    [Fact]
    public void Bookmark_PrecedesBackup_AndCollectionFollowsTransactionDisposal()
    {
        _messages.Records.Add(new() { Text = "old", Level = 4 });
        _onBackup = () =>
        {
            Assert.Equal(1, _messages.Reads);
            _messages.Records.Add(new() { Text = "backup", Index = "S123456", Level = 2 });
        };
        _messages.BeforeRead = () =>
        {
            if (_messages.Reads == 2)
            {
                Assert.Equal("Dispose", _transactions.Calls.Last());
            }
        };

        CableGenerateData result = Execute();

        SystemMessage message = Assert.Single(result.EplanMessages);
        Assert.Equal((1, "S123456", "backup"), (result.EplanMessageCount, message.Number, message.Text));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedAction_IncludesLimitedNewMessagesAndFullCount_AfterRollbackOrUnknownOutcome(bool rollbackFails)
    {
        _messages.Records.Add(new() { Text = "old", Level = 4 });
        _transactions.FailAt = rollbackFails ? "Rollback" : null;
        _generator.Run = () =>
        {
            _messages.Records.AddRange(Enumerable.Range(0, 25).Select(_ => new SystemMessageRecord { Text = "Generation refused.", Index = "S123456", Level = 4 }));
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Action refused.");
        };

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute()).Error;

        Assert.Equal(rollbackFails ? (ErrorReason.OutcomeUnknown, ExecutionState.Unknown) : (ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        var messages = JArray.Parse(error.Metadata!["eplanMessages"]);
        Assert.Equal((20, "25"), (messages.Count, error.Metadata["eplanMessageCount"]));
        Assert.All(messages, m => Assert.Equal(("S123456", "Generation refused."), ((string?)m["number"], (string?)m["text"])));
        Assert.Equal("Dispose", _transactions.Calls.Last());
        Assert.DoesNotContain("Commit", _transactions.Calls);
        Assert.Equal(2, _messages.Reads);
    }

    [Fact]
    public void CollectionFailureAfterCommit_IsAWarning_AndKeepsPostCommitWarning()
    {
        _messages.FailAt = 2;
        _transactions.FailAt = "CloseUndo";

        CableGenerateData result = Execute();

        Assert.Contains("Commit", _transactions.Calls);
        Assert.DoesNotContain("Rollback", _transactions.Calls);
        Assert.Contains("messages could not be read", result.Warning, StringComparison.Ordinal);
        Assert.Contains("committed", result.Warning, StringComparison.Ordinal);
        Assert.Empty(result.EplanMessages);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void MessageReadFailure_WithFailedAction_PreservesRolledBackErrorAndAddsWarning(int failAt)
    {
        _messages.FailAt = failAt;
        _generator.Run = () => throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Action refused.");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute()).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal("Action refused.", error.Message);
        Assert.Empty(JArray.Parse(error.Metadata!["eplanMessages"]));
        Assert.Contains("messages could not be read", error.Metadata["warning"], StringComparison.Ordinal);
    }

    [Fact]
    public void BookmarkFailure_IsOnlyAWarning_AndDoesNotReturnOldSessionMessages()
    {
        _messages.FailAt = 1;
        _messages.Records.Add(new() { Text = "old", Level = 4 });

        CableGenerateData result = Execute();

        Assert.Contains("Commit", _transactions.Calls);
        Assert.DoesNotContain("Rollback", _transactions.Calls);
        Assert.Contains("messages could not be read", result.Warning, StringComparison.Ordinal);
        Assert.Empty(result.EplanMessages);
        Assert.Equal(1, _messages.Reads);
    }

    [Fact]
    public void Cancellation_AfterAction_ReturnsMessagesWithoutChangingCancelledState()
    {
        var context = new OperationContext("cancel", write: true, progress: null);
        _generator.Run = () =>
        {
            _messages.Records.Add(new() { Text = "before cancellation", Level = 2 });
            context.Cancel();
        };

        BridgeError error = Assert.Throws<BridgeErrorException>(() => context.Run(Execute)).Error;

        Assert.Equal((ErrorReason.Cancelled, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal("before cancellation", (string?)Assert.Single(JArray.Parse(error.Metadata!["eplanMessages"]))["text"]);
    }

    private CableGenerateData Execute()
    {
        var args = new CableGenerateArgs { Project = "Line1", CreationScheme = "Standard", NumberingScheme = "Standard", SelectionScheme = "Standard" };
        args.Fingerprint = ((WritePlan)new CableGeneratePlanOperation(_catalog, _generator).Execute(args)!).Fingerprint;
        return (CableGenerateData)new CableGenerateExecuteOperation(_catalog, new FakePageReader(), _generator, new FakeProjectBackup(), _transactions, _messages)
        {
            MakeBackup = (_, _) => _onBackup?.Invoke(),
        }.Execute(args)!;
    }

    private sealed class Generator : ICableGenerator
    {
        public Action Run { get; set; } = () => { };
        public CableGenerationSnapshot Read(string projectPath) => new([], 0, "state");
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Schemes(string projectPath) => new Dictionary<string, IReadOnlyList<string>>
        {
            ["creationScheme"] = ["Standard"],
            ["numberingScheme"] = ["Standard"],
            ["selectionScheme"] = ["Standard"],
        };

        public void Generate(string projectPath, string creationScheme, string numberingScheme, string selectionScheme) => Run();
    }

    private sealed class Messages : ISystemMessageReader
    {
        public List<SystemMessageRecord> Records { get; } = [];
        public int Reads { get; private set; }
        public int FailAt { get; set; }
        public Action? BeforeRead { get; set; }

        public IReadOnlyList<SystemMessageRecord> ReadAll()
        {
            Reads++;
            BeforeRead?.Invoke();
            return Reads == FailAt ? throw new InvalidOperationException("Messages unavailable.") : Records.ToArray();
        }
    }
}
