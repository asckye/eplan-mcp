using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// The write-back of a project's language settings after a change that did not commit (docs/tools/eplan_language_set.md
/// and eplan_language_remove.md, section 5): SafetyPoint does not take project settings back (spike S23, api-notes A28),
/// so the fake's rollback leaves them as the change left them, and only the write-back restores them.
/// </summary>
[Trait("Size", "Small")]
public sealed class LanguageTransactionTests
{
    private const string Tool = "eplan_language_set";
    private const string Path = @"D:\Scratch\Line1";
    private const string Restore = "Ask the engineer.";

    private readonly FakeProjectLanguages _languages = new();
    private readonly FakeTransactions _transactions = new();
    private readonly LanguageSettings _before;

    public LanguageTransactionTests() => _before = _languages.Read(Path);

    [Fact]
    public void AChangeThatPasses_IsCommittedAndWritesNothingBack()
    {
        Committed<string> committed = Run(() =>
        {
            _languages.SetDisplayed(Path, "en_US");
            return "done";
        });

        Assert.Equal(("done", null), (committed.Result, committed.Warning));
        Assert.Equal(["SetDisplayed en_US"], _languages.Calls);
        Assert.Equal(["Begin AI: eplan_language_set", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void AChangeThatFails_IsRolledBackThenTheSettingsWrittenBack()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Run<int>(() =>
        {
            _languages.Add(Path, "sv_SE");
            _languages.SetDisplayed(Path, "sv_SE");
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN failed.");
        })).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal(["Begin AI: eplan_language_set", "CloseUndo", "Rollback", "Dispose"], _transactions.Calls);
        Assert.Equal("Write de_DE;en_US;zh_CN; ##_##;", _languages.Calls[^1]);
        Assert.True(LanguageTransaction.Same(_languages.Settings, _before));
    }

    [Fact]
    public void AnEplanExceptionAfterAChange_IsRethrownAsItIsOnceTheSettingsAreBack()
    {
        var failure = new InvalidOperationException("EPLAN failed");

        Exception thrown = Assert.Throws<InvalidOperationException>(() => Run<int>(() =>
        {
            _languages.SetDisplayed(Path, "en_US");
            throw failure;
        }));

        Assert.Same(failure, thrown);
        Assert.Equal("##_##;", _languages.Settings.Display);
    }

    [Fact]
    public void AFailureThatChangedNothing_WritesNothing()
    {
        Assert.Throws<InvalidOperationException>(() => Run<int>(() => throw new InvalidOperationException("EPLAN failed")));

        Assert.Empty(_languages.Calls);
    }

    [Fact]
    public void AWriteBackThatFails_IsOutcomeUnknownWithTheSettingsAsTheyWereAndTheAdvice()
    {
        _languages.WriteFailure = new InvalidOperationException("the setting cannot be written");

        BridgeErrorException thrown = Assert.Throws<BridgeErrorException>(() => Run<int>(() =>
        {
            _languages.Add(Path, "sv_SE");
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN failed.");
        }));

        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown, false), (thrown.Error.Reason, thrown.Error.Execution, thrown.Error.Retryable));
        Assert.Equal("eplan_language_set failed, and the project's language settings could not be written back as they were, so they may differ from before. Do not call this tool again. Ask the engineer. metadata.translation and metadata.display hold the settings as they were.", thrown.Message);
        Assert.Equal(("de_DE\nen_US\nzh_CN", "##_##"), (thrown.Error.Metadata!["translation"], thrown.Error.Metadata["display"]));
        Assert.Same(_languages.WriteFailure, thrown.InnerException);
    }

    [Fact]
    public void AWriteBackThatReadsBackOtherwise_IsOutcomeUnknown()
    {
        _languages.IgnoreWrite = true;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Run<int>(() =>
        {
            _languages.SetDisplayed(Path, "en_US");
            throw new InvalidOperationException("EPLAN failed");
        })).Error;

        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Reason, error.Execution));
    }

    [Fact]
    public void ARollbackThatFails_NamesTheSettingsAndWritesNothing()
    {
        _transactions.FailAt = "Rollback";

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Run<int>(() =>
        {
            _languages.SetDisplayed(Path, "en_US");
            throw new InvalidOperationException("EPLAN failed");
        })).Error;

        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Reason, error.Execution));
        Assert.Equal(("de_DE\nen_US\nzh_CN", "##_##"), (error.Metadata!["translation"], error.Metadata["display"]));
        Assert.Equal(["SetDisplayed en_US"], _languages.Calls);
    }

    [Fact]
    public void ACancelFoundAtTheLastSafePoint_RollsBackAndWritesTheSettingsBack()
    {
        var router = new OperationRouter(
            [new DelegateWrite(() => Run(() =>
            {
                _languages.SetDisplayed(Path, "en_US");
                return "done";
            }))],
            new InlineDispatcher(),
            null,
            new ListLog(),
            TimeSpan.FromSeconds(10));
        _languages.OnChange = _ => router.Invoke(OperationNames.OperationCancel, new JObject { ["requestId"] = "r1" }, "cancel");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => router.Invoke(DelegateWrite.WriteName, null, "r1")).Error;

        Assert.Equal((ErrorReason.Cancelled, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal(["SetDisplayed en_US", "Write de_DE;en_US;zh_CN; ##_##;"], _languages.Calls);
    }

    [Theory]
    [InlineData("de_DE;en_US;", "##_##;", true)]
    [InlineData("de_DE;en_US", "##_##", true)]
    [InlineData("en_US;de_DE;", "##_##;", false)]
    [InlineData("de_DE;en_US;", "en_US;", false)]
    public void Same_ComparesTheLanguagesInOrderNotTheStoredText(string translation, string display, bool same) =>
        Assert.Equal(same, LanguageTransaction.Same(new LanguageSettings { Translation = translation, Display = display }, new LanguageSettings { Translation = "de_DE;en_US;", Display = "##_##;" }));

    private Committed<T> Run<T>(Func<T> change) => LanguageTransaction.Run(_transactions, _languages, Tool, Path, _before, Restore, change);

    /// <summary>A write operation that runs a delegate, so the router's cancel reaches it.</summary>
    private sealed class DelegateWrite(Func<object?> execute) : IWriteOperation
    {
        public const string WriteName = "language.set.execute";

        public string Name => WriteName;

        public object ParseArgs(JToken? args) => new();

        public object? Execute(object args) => execute();
    }
}
