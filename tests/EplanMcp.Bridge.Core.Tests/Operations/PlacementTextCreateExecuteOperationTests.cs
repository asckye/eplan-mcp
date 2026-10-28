using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlacementTextCreateExecuteOperationTests
{
    private readonly PlacementTextStore _store = new();

    [Fact]
    public void Create_MultilingualText_VerifiesAndCommitsOneUndoStep()
    {
        PlacementTextCreateArgs args = PlacementTextStore.Args();
        PlacementTextCreateData data = Execute(args);
        Assert.True(JToken.DeepEquals(BridgeJson.FromObject(args.Text), BridgeJson.FromObject(data.Text)));
        Assert.Equal([12.5, 22.5], data.Text.Position);
        Assert.Equal(3.5, data.Text.HeightMm);
        Assert.Null(data.Warning);
        Assert.Single(_store.Texts);
        Assert.Equal(1, _store.Validations);
        Assert.Equal(["Begin AI: eplan_placement_create", "Commit", "CloseUndo", "Dispose"], _store.Transactions.Calls);
    }

    [Theory]
    [InlineData("height")]
    [InlineData("x")]
    [InlineData("y")]
    [InlineData("missing-language")]
    [InlineData("extra-language")]
    [InlineData("contents")]
    public void Create_ReadbackDiffers_RollsBackWithoutClosingUndo(string mismatch)
    {
        _store.Readback = t =>
        {
            switch (mismatch)
            {
                case "height":
                    t.HeightMm += 0.01;
                    break;
                case "x":
                    t.Position[0] += 0.01;
                    break;
                case "y":
                    t.Position[1] += 0.01;
                    break;
                case "missing-language":
                    t.Contents.Remove("de_DE");
                    break;
                case "extra-language":
                    t.Contents["fr_FR"] = "Panneau";
                    break;
                case "contents":
                    t.Contents["en_US"] = "Changed";
                    break;
            }
            return t;
        };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(PlacementTextStore.Args())).Error;
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Empty(_store.Texts);
        Assert.Equal(["Begin AI: eplan_placement_create", "Rollback", "Dispose"], _store.Transactions.Calls);
    }

    [Theory]
    [InlineData("display-language", ErrorReason.InvalidArgument)]
    [InlineData("unknown-language", ErrorReason.InvalidArgument)]
    [InlineData("not-circuit", ErrorReason.InvalidArgument)]
    [InlineData("missing-page", ErrorReason.PageNotFound)]
    public void Create_AdapterPreflightRefuses_NeverStartsTransaction(string problem, string reason)
    {
        _store.ValidationFailure = new(reason, problem);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(PlacementTextStore.Args())).Error;
        Assert.Equal((reason, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_store.Transactions.Calls);
        Assert.Equal(0, _store.Writes);
    }

    [Theory]
    [InlineData("project")]
    [InlineData("page")]
    [InlineData("readonly")]
    [InlineData("null-text")]
    [InlineData("null-position")]
    [InlineData("position-count")]
    [InlineData("nan")]
    [InlineData("infinity")]
    [InlineData("negative")]
    [InlineData("too-far")]
    [InlineData("height-nan")]
    [InlineData("height-infinity")]
    [InlineData("height-small")]
    [InlineData("height-large")]
    [InlineData("null-contents")]
    [InlineData("empty-contents")]
    [InlineData("empty-text")]
    [InlineData("null-entry")]
    [InlineData("large-text")]
    [InlineData("invalid-language")]
    public void Create_InvalidValues_AreNotStarted(string problem)
    {
        PlacementTextCreateArgs args = PlacementTextStore.Args();
        switch (problem)
        {
            case "project":
                args.Project = " ";
                break;
            case "page":
                args.Page = "";
                break;
            case "readonly":
                _store.Project.ReadOnly = true;
                break;
            case "null-text":
                args.Text = null!;
                break;
            case "null-position":
                args.Text.Position = null!;
                break;
            case "position-count":
                args.Text.Position = [1];
                break;
            case "nan":
                args.Text.Position[0] = double.NaN;
                break;
            case "infinity":
                args.Text.Position[0] = double.NegativeInfinity;
                break;
            case "negative":
                args.Text.Position[0] = -1;
                break;
            case "too-far":
                args.Text.Position[0] = 10001;
                break;
            case "height-nan":
                args.Text.HeightMm = double.NaN;
                break;
            case "height-infinity":
                args.Text.HeightMm = double.PositiveInfinity;
                break;
            case "height-small":
                args.Text.HeightMm = 0.49;
                break;
            case "height-large":
                args.Text.HeightMm = 100.01;
                break;
            case "null-contents":
                args.Text.Contents = null!;
                break;
            case "empty-contents":
                args.Text.Contents.Clear();
                break;
            case "empty-text":
                args.Text.Contents["en_US"] = " ";
                break;
            case "null-entry":
                args.Text.Contents["en_US"] = null!;
                break;
            case "large-text":
                args.Text.Contents["en_US"] = new('a', 500);
                break;
            case "invalid-language":
                args.Text.Contents["en_US\n"] = "A";
                break;
        }
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Equal(0, _store.Validations);
        Assert.Empty(_store.Transactions.Calls);
    }

    [Theory]
    [InlineData("/extra", "/extra")]
    [InlineData("/text/extra", "/text/extra")]
    [InlineData("/text/a~/b", "/text/a~0~1b")]
    public void ParseArgs_UnknownMembers_UseJsonPointers(string member, string expected)
    {
        JObject args = (JObject)BridgeJson.FromObject(PlacementTextStore.Args());
        if (member.StartsWith("/text/", StringComparison.Ordinal))
        { args["text"]![member[6..]] = true; }
        else
        { args[member[1..]] = true; }
        Assert.Contains(expected, Assert.Throws<BridgeErrorException>(() => _store.Operation.ParseArgs(args)).Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"project\":1,\"page\":\"P\",\"text\":{\"position\":[1,2],\"heightMm\":3,\"contents\":{\"en_US\":\"Panel\"}}}")]
    [InlineData("{\"project\":\"Line1\",\"page\":\"P\",\"text\":{\"position\":[\"1\",2],\"heightMm\":3,\"contents\":{\"en_US\":\"Panel\"}}}")]
    [InlineData("{\"project\":\"Line1\",\"page\":\"P\",\"text\":{\"position\":[1,2],\"heightMm\":\"3\",\"contents\":{\"en_US\":\"Panel\"}}}")]
    [InlineData("{\"project\":\"Line1\",\"page\":\"P\",\"text\":{\"position\":[1e400,2],\"heightMm\":3,\"contents\":{\"en_US\":\"Panel\"}}}")]
    [InlineData("{\"project\":\"Line1\",\"page\":\"P\",\"text\":{\"position\":[1,2],\"heightMm\":3,\"contents\":{\"en_US\":1}}}")]
    public void ParseArgs_InvalidShape_RejectsCoercion(string json)
    {
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => _store.Operation.ParseArgs(JToken.Parse(json))).Error.Reason);
        Assert.Empty(_store.Transactions.Calls);
    }

    [Fact]
    public void Create_NativeFailureAfterWrite_RestoresState()
    {
        _store.WriteFailure = new InvalidOperationException("native failure");
        Assert.Throws<InvalidOperationException>(() => Execute(PlacementTextStore.Args()));
        Assert.Empty(_store.Texts);
        Assert.Equal(["Begin AI: eplan_placement_create", "Rollback", "Dispose"], _store.Transactions.Calls);
    }

    [Theory]
    [InlineData("Commit")]
    [InlineData("Rollback")]
    public void Create_TransactionOutcomeUncertain_ReportsUnknown(string step)
    {
        _store.Transactions.FailAt = step;
        if (step == "Rollback")
        { _store.WriteFailure = new InvalidOperationException("native failure"); }
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(PlacementTextStore.Args())).Error;
        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Reason, error.Execution));
    }

    [Fact]
    public void Create_CloseUndoFails_KeepsCommitAndWarns()
    {
        _store.Transactions.FailAt = "CloseUndo";
        PlacementTextCreateData result = Execute(PlacementTextStore.Args());
        Assert.NotNull(result.Warning);
        Assert.Single(_store.Texts);
        Assert.DoesNotContain("Rollback", _store.Transactions.Calls);
    }

    private PlacementTextCreateData Execute(PlacementTextCreateArgs args) => (PlacementTextCreateData)_store.Operation.Execute(args)!;

    [Fact]
    public void Router_CancelAfterNativeCreate_RollsBackBeforeCommitAndRecordsOutcome()
    {
        var router = new OperationRouter([_store.Operation], new InlineDispatcher(), null, new ListLog(), TimeSpan.FromSeconds(1));
        _store.AfterWrite = () => router.Invoke(OperationNames.OperationCancel, new JObject { ["requestId"] = "write" }, "cancel");
        BridgeError error = Assert.Throws<BridgeErrorException>(() => router.Invoke(OperationNames.PlacementTextCreateExecute,
            BridgeJson.FromObject(PlacementTextStore.Args()), "write")).Error;
        Assert.Equal((ErrorReason.Cancelled, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Empty(_store.Texts);
        Assert.Equal(["Begin AI: eplan_placement_create", "Rollback", "Dispose"], _store.Transactions.Calls);
        Assert.Equal(OutcomeStatus.RolledBack, (string?)router.Invoke(OperationNames.OutcomeGet, new JObject { ["requestId"] = "write" }, "query")["status"]);
    }
}
