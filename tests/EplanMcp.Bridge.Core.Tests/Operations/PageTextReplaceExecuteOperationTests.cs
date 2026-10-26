using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using EplanMcp.Tests;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PageTextReplaceExecuteOperationTests
{
    private readonly PageTextStore _store = new();

    [Fact]
    public void Execute_MultiplePages_CommitsOneUndoStepAndKeepsOtherLanguages()
    {
        PageTextReplaceArgs args = Plan();
        PageFreeText before = _store.Pages[PageTextStore.Page].Texts[0];

        PageTextReplaceData result = Execute(args);

        Assert.Equal("Line1", result.Project);
        Assert.Null(result.Warning);
        Assert.Equal(3, result.Changes.Count);
        Assert.Equal(4, result.Changes.Sum(c => c.Occurrences));
        Assert.Equal(["Begin AI: eplan_page_text_replace", "Commit", "CloseUndo", "Dispose"], _store.Calls);
        Assert.Equal(3, _store.Writes.Count);
        Assert.Equal(4, _store.Reads);
        Assert.Equal("Drive Drive", _store.Pages[PageTextStore.Page].Texts[0].Entries[0].Text);
        Assert.Equal("Drive", _store.Pages[PageTextStore.OtherPage].Texts[0].Entries[0].Text);
        Assert.Equal(before.Entries.Skip(1), _store.Pages[PageTextStore.Page].Texts[0].Entries.Skip(1), new EntryComparer());
    }

    [Fact]
    public void Execute_AllLanguages_WritesEachObjectOnceAndPreservesEmptyEntries()
    {
        PageTextReplaceArgs args = Plan(allLanguages: true);
        PageTextReplaceData result = Execute(args);

        Assert.Equal(4, result.Changes.Count);
        Assert.Equal(3, _store.Writes.Count);
        PageFreeText first = _store.Pages[PageTextStore.Page].Texts[0];
        Assert.Equal(["Drive Drive", "Drive", "Free", string.Empty], first.Entries.Select(e => e.Text));
    }

    [Fact]
    public void Execute_IndependentTextAndEmptyReplacement_KeepStoredLanguageEntry()
    {
        PageTextReplaceArgs args = PageTextStore.Args();
        args.Find = "Free";
        args.Replace = string.Empty;
        args.Language = "??_??";
        args.Fingerprint = ((WritePlan)new PageTextReplacePlanOperation(_store, _store).Execute(args)!).Fingerprint;

        PageTextChange change = Assert.Single(Execute(args).Changes);

        Assert.Equal(("??_??", "Free", string.Empty), (change.Language, change.Before, change.After));
        Assert.Single(_store.Writes);
        Assert.Equal(["Motor Motor", "Motor", string.Empty, string.Empty], _store.Pages[PageTextStore.Page].Texts[0].Entries.Select(e => e.Text));
        Assert.Equal("Motor", _store.Pages[PageTextStore.OtherPage].Texts[0].Entries[0].Text);
    }

    [Theory]
    [InlineData("content")]
    [InlineData("other-language")]
    [InlineData("no-matches")]
    [InlineData("removed-text")]
    [InlineData("added-text")]
    [InlineData("page-identity")]
    [InlineData("text-identity")]
    [InlineData("removed-page")]
    [InlineData("cap-exceeded")]
    public void Execute_ContentOrInventoryChanged_RefusesStalePlan(string change)
    {
        PageTextReplaceArgs args = Plan();
        PageTextSnapshot page = _store.Pages[PageTextStore.Page];
        PageFreeText first = page.Texts[0];
        switch (change)
        {
            case "content":
                ReplaceFirst(first.Entries.Select(e => new TextEntry(e.Language, e.Text + " later")));
                break;
            case "other-language":
                ReplaceFirst(first.Entries.Append(new TextEntry("fr_FR", "Moteur")));
                break;
            case "no-matches":
                foreach (PageTextSnapshot item in _store.Pages.Values.ToList())
                {
                    _store.Pages[item.Name] = new(item.Name, item.Identifier,
                        [.. item.Texts.Select(t => new PageFreeText(t.Oid, t.Identifier,
                            [.. t.Entries.Select(e => new TextEntry(e.Language, e.Text.Replace("Motor", "Gone", StringComparison.Ordinal)))]))]);
                }
                break;
            case "removed-text":
                _store.Pages[page.Name] = new(page.Name, page.Identifier, [first]);
                break;
            case "added-text":
                _store.Pages[page.Name] = new(page.Name, page.Identifier, [.. page.Texts, PageTextStore.Text(99, new TextEntry("en_US", "Unmatched"))]);
                break;
            case "page-identity":
                _store.Pages[page.Name] = new(page.Name, "different-page", page.Texts);
                break;
            case "text-identity":
                _store.Pages[page.Name] = new(page.Name, page.Identifier, [new(first.Oid, "different-text", first.Entries), page.Texts[1]]);
                break;
            case "removed-page":
                _store.Pages.Remove(page.Name);
                break;
            case "cap-exceeded":
                args.MaxChanges = 3;
                _store.Pages[page.Name] = new(page.Name, page.Identifier, [.. page.Texts, PageTextStore.Text(99, new TextEntry("en_US", "Motor"))]);
                break;
        }

        string before = Inventory();
        BridgeError error = Failure(args);

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted, false), (error.Reason, error.Execution, error.Retryable));
        Assert.Empty(_store.Calls);
        Assert.Empty(_store.Writes);
        Assert.Equal(before, Inventory());
    }

    [Theory]
    [InlineData("en_US")]
    [InlineData("de_DE")]
    [InlineData("zh_CN")]
    [InlineData("new-language")]
    [InlineData("duplicate-language")]
    [InlineData("identity")]
    [InlineData("oid")]
    public void Execute_ReadbackMismatch_RollsBackAllPages(string mismatch)
    {
        PageTextReplaceArgs args = Plan();
        string before = Inventory();
        _store.Readback = text => text.Oid != 3 ? text : mismatch switch
        {
            "identity" => new(text.Oid, "wrong-identity", text.Entries),
            "oid" => new(99, text.Identifier, text.Entries),
            "new-language" => new(text.Oid, text.Identifier, [.. text.Entries, new("fr_FR", "Extra")]),
            "duplicate-language" => new(text.Oid, text.Identifier, [.. text.Entries, text.Entries[0]]),
            _ => new(text.Oid, text.Identifier, [new("en_US", mismatch == "en_US" ? "Wrong" : "Drive"), new(mismatch, "Unexpected")]),
        };

        BridgeError error = Failure(args);

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal(PageTextStore.OtherPage, error.Metadata!["page"]);
        Assert.Equal("3", error.Metadata["oid"]);
        Assert.Equal(3, _store.Writes.Count);
        Assert.Equal(["Begin AI: eplan_page_text_replace", "CloseUndo", "Rollback", "Dispose"], _store.Calls);
        Assert.Equal(before, Inventory());
    }

    [Theory]
    [InlineData("zh_CN")]
    [InlineData("??_??")]
    public void Execute_LostEmptyOrIndependentTranslation_RollsBack(string language)
    {
        PageTextReplaceArgs args = Plan();
        string before = Inventory();
        _store.Readback = t => new(t.Oid, t.Identifier, [.. t.Entries.Where(e => e.Language != language)]);

        BridgeError error = Failure(args);

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal(language, error.Metadata!["language"]);
        Assert.Equal(before, Inventory());
    }

    [Fact]
    public void Execute_AdapterThrowsAfterPartialWrite_RollsBackAndRouterRecordsOutcome()
    {
        PageTextReplaceArgs args = Plan();
        string before = Inventory();
        _store.WriteFailure = new InvalidOperationException("internal native failure");
        OperationRouter router = Router();

        BridgeError error = Assert.Throws<BridgeErrorException>(() => router.Invoke(OperationNames.PageTextReplaceExecute, BridgeJson.FromObject(args), "write")).Error;

        Assert.Equal((ErrorReason.Internal, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.DoesNotContain("native failure", error.Message, StringComparison.Ordinal);
        Assert.Equal(OutcomeStatus.RolledBack, router.Invoke(OperationNames.OutcomeGet, BridgeJson.FromObject(new OutcomeGetArgs { RequestId = "write" }), "get")["status"]!.ToString());
        Assert.Equal(before, Inventory());
    }

    [Fact]
    public void Execute_KnownReadFailure_IsNotStartedWithoutTransaction()
    {
        PageTextReplaceArgs args = Plan();
        _store.ReadFailure = new BridgeErrorException(ErrorReason.EplanOperationFailed, "Incomplete inventory.");

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.NotStarted), Reason(Failure(args)));
        Assert.Empty(_store.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Execute_MissingFingerprint_RefusesBeforeReading(string? fingerprint)
    {
        PageTextReplaceArgs args = PageTextStore.Args();
        args.Fingerprint = fingerprint;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), Reason(Failure(args)));
        Assert.Equal(0, _store.Reads);
        Assert.Empty(_store.Calls);
    }

    [Fact]
    public void Execute_CancelledDuringWrite_RollsBack()
    {
        PageTextReplaceArgs args = Plan();
        string before = Inventory();
        var context = new OperationContext("write", write: true, progress: null);
        _store.AfterWrite = context.Cancel;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => context.Run(() => Execute(args))).Error;

        Assert.Equal((ErrorReason.Cancelled, ExecutionState.RolledBack), Reason(error));
        Assert.Single(_store.Writes);
        Assert.Equal(before, Inventory());
    }

    [Theory]
    [InlineData("Commit")]
    [InlineData("Rollback")]
    public void Execute_TransactionFailure_IsUnknown(string step)
    {
        PageTextReplaceArgs args = Plan();
        _store.FailTransaction = step;
        if (step == "Rollback")
        {
            _store.WriteFailure = new InvalidOperationException("write failed");
        }

        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), Reason(Failure(args)));
    }

    [Fact]
    public void Execute_UndoCloseFailure_ReturnsCommittedDataWithWarning()
    {
        PageTextReplaceArgs args = Plan();
        _store.FailTransaction = "CloseUndo";

        PageTextReplaceData data = Execute(args);

        Assert.Equal(3, data.Changes.Count);
        Assert.NotNull(data.Warning);
        Assert.Contains("Commit", _store.Calls);
        Assert.DoesNotContain("Rollback", _store.Calls);
    }

    [Fact]
    public void Execute_ReplansWithinOneDispatcherCall_AndRejectsRepeatedRequestId()
    {
        var dispatcher = new InlineDispatcher();
        var router = new OperationRouter(
            [new PageTextReplacePlanOperation(_store, _store), new PageTextReplaceExecuteOperation(_store, _store, _store)],
            dispatcher, null, new ListLog(), TimeSpan.FromSeconds(1));
        PageTextReplaceArgs args = PageTextStore.Args();
        args.Fingerprint = router.Invoke(OperationNames.PageTextReplacePlan, BridgeJson.FromObject(args), "plan")["fingerprint"]!.ToString();

        router.Invoke(OperationNames.PageTextReplaceExecute, BridgeJson.FromObject(args), "write");

        Assert.Equal(2, dispatcher.Calls);
        Assert.Equal(3, _store.Writes.Count);
        BridgeError duplicate = Assert.Throws<BridgeErrorException>(() => router.Invoke(OperationNames.PageTextReplaceExecute, BridgeJson.FromObject(args), "write")).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), Reason(duplicate));
        Assert.Equal(3, _store.Writes.Count);
    }

    private PageTextReplaceArgs Plan(bool allLanguages = false)
    {
        PageTextReplaceArgs args = PageTextStore.Args();
        args.Language = allLanguages ? "all" : "en_US";
        args.Fingerprint = ((WritePlan)new PageTextReplacePlanOperation(_store, _store).Execute(args)!).Fingerprint;
        return args;
    }

    private PageTextReplaceData Execute(PageTextReplaceArgs args) =>
        (PageTextReplaceData)new PageTextReplaceExecuteOperation(_store, _store, _store).Execute(args)!;

    private BridgeError Failure(PageTextReplaceArgs args) => Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

    private static (string Reason, string? Execution) Reason(BridgeError error) => (error.Reason, error.Execution);

    private string Inventory() => BridgeJson.FromObject(_store.Pages).ToString();

    private void ReplaceFirst(IEnumerable<TextEntry> entries)
    {
        PageTextSnapshot page = _store.Pages[PageTextStore.Page];
        _store.Pages[page.Name] = new(page.Name, page.Identifier, [new(page.Texts[0].Oid, page.Texts[0].Identifier, [.. entries]), page.Texts[1]]);
    }

    private OperationRouter Router() => new([new PageTextReplaceExecuteOperation(_store, _store, _store)], new InlineDispatcher(), null, new ListLog(), TimeSpan.FromSeconds(1));

    private sealed class EntryComparer : IEqualityComparer<TextEntry>
    {
        public bool Equals(TextEntry? x, TextEntry? y) => x?.Language == y?.Language && x?.Text == y?.Text;

        public int GetHashCode(TextEntry obj) => HashCode.Combine(obj.Language, obj.Text);
    }
}
