using System.Globalization;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PageTextReplacePlanOperationTests
{
    private const string Page = "=F+A2&EFA1/2";
    private readonly OpenProject _project = new() { Name = "Line1", Path = @"D:\Scratch\Line1" };
    private readonly FakePageTextReader _reader = new();

    public PageTextReplacePlanOperationTests()
    {
        _reader.Pages[Page] = Snapshot(
            Text(2, new TextEntry("de_DE", "Motor"), new TextEntry("en_US", "Motor Motor"), new TextEntry("??_??", "Motor")),
            Text(1, new TextEntry("en_US", "Motor"), new TextEntry("zh_CN", "电机")));
    }

    [Fact]
    public void Plan_ExplicitLanguage_KeepsOtherStoredLanguages()
    {
        string before = Inventory();
        WritePlan plan = Plan();
        List<PageTextChange> changes = Assert.IsType<List<PageTextChange>>(plan.PageTextChanges);

        Assert.Equal((_project.Name, _project.Path), (plan.Project, plan.ProjectPath));
        Assert.Equal([1L, 2L], changes.Select(c => c.Oid));
        Assert.Equal(["en_US", "en_US"], changes.Select(c => c.Language));
        Assert.Equal([1, 2], changes.Select(c => c.Occurrences));
        Assert.Equal(["Motor", "Motor Motor"], changes.Select(c => c.Before));
        Assert.Equal(["Drive", "Drive Drive"], changes.Select(c => c.After));
        Assert.All(changes, c => Assert.Equal(Page, c.Page));
        Assert.Equal(("update", "page", Page, "text 1 (en_US)", "Motor", "Drive"),
            (plan.Changes[0].Action, plan.Changes[0].Kind, plan.Changes[0].Name, plan.Changes[0].Property, plan.Changes[0].OldValue, plan.Changes[0].NewValue));
        Assert.Equal(before, Inventory());
        Assert.Equal([(_project.Path, Page)], _reader.Reads);
    }

    [Fact]
    public void Plan_AllLanguages_IncludesIndependentAndEmptyEntries()
    {
        _reader.Pages[Page] = Snapshot(Text(1,
            new TextEntry("en_US", "Motor"), new TextEntry("de_DE", string.Empty), new TextEntry("??_??", "Motor")));
        string before = Inventory();

        WritePlan plan = Plan(Args(language: "all"));

        Assert.Equal(["??_??", "en_US"], plan.PageTextChanges!.Select(c => c.Language));
        Assert.Equal(before, Inventory());
        Assert.NotEqual(plan.Fingerprint, Hash(Snapshot(Text(1, new TextEntry("en_US", "Motor"), new TextEntry("??_??", "Motor")))));
    }

    [Fact]
    public void Plan_IndependentLanguage_IsExplicitAndDoesNotUseFallback()
    {
        PageTextChange change = Assert.Single(Plan(Args(language: "??_??")).PageTextChanges!);

        Assert.Equal((2, "??_??", "Motor", "Drive"), (change.Oid, change.Language, change.Before, change.After));
        Assert.Equal(ErrorReason.InvalidArgument, Error(Args(language: "zh_CN")).Reason);
    }

    [Fact]
    public void Plan_LanguageCode_IsCanonicalizedWithoutMutatingArguments()
    {
        PageTextReplaceArgs args = Args(language: "EN_us");

        Assert.All(Plan(args).PageTextChanges!, c => Assert.Equal("en_US", c.Language));
        Assert.Equal("EN_us", args.Language);
    }

    [Fact]
    public void Plan_CaseInsensitive_ChangesCaseAndSkipsUnchangedEntries()
    {
        _reader.Pages[Page] = Snapshot(Text(1, new TextEntry("en_US", "MOTOR")), Text(2, new TextEntry("en_US", "Motor motor")));
        PageTextReplaceArgs args = Args();
        args.Find = "motor";
        args.Replace = "MOTOR";
        args.CaseSensitive = false;

        PageTextChange change = Assert.Single(Plan(args).PageTextChanges!);

        Assert.Equal((2, "MOTOR MOTOR", 2), (change.Oid, change.After, change.Occurrences));
    }

    [Theory]
    [InlineData("Absent", "Drive", "en_US")]
    [InlineData("MOTOR", "Drive", "en_US")]
    [InlineData("Motor", "Drive", "zh_CN")]
    [InlineData("Motor", "Motor", "en_US")]
    public void Plan_NoChanges_RefusesWithoutTransaction(string find, string replacement, string language)
    {
        PageTextReplaceArgs args = Args(language);
        args.Find = find;
        args.Replace = replacement;
        string before = Inventory();

        Assert.Equal(ErrorReason.InvalidArgument, Error(args).Reason);
        Assert.Equal(before, Inventory());
        Assert.False((object)Operation() is IWriteOperation);
    }

    [Fact]
    public void Plan_EmptyReplacement_RemovesOnlyMatchedLiteral()
    {
        PageTextReplaceArgs args = Args();
        args.Replace = string.Empty;

        Assert.Equal([string.Empty, " "], Plan(args).PageTextChanges!.Select(c => c.After));
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void Plan_ChangeCap_RefusesEntireBatch(int count, bool allowed)
    {
        _reader.Pages[Page] = Snapshot([.. Enumerable.Range(1, count).Select(n => Text(n, new TextEntry("en_US", "Motor")))]);
        string before = Inventory();

        if (allowed)
        {
            Assert.Equal(count, Plan().PageTextChanges!.Count);
        }
        else
        {
            BridgeError error = Error(Args());
            Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
            Assert.Equal("500", error.Metadata!["limit"]);
            Assert.Equal("501", error.Metadata["foundAtLeast"]);
        }

        Assert.Equal(before, Inventory());
    }

    [Fact]
    public void Plan_ChangeCap_CountsObjectLanguagesRatherThanOccurrences()
    {
        _reader.Pages[Page] = Snapshot(Text(1, new TextEntry("en_US", string.Concat(Enumerable.Repeat("Motor", 501))), new TextEntry("de_DE", "Motor")));
        PageTextReplaceArgs args = Args();
        args.MaxChanges = 1;

        Assert.Equal(501, Assert.Single(Plan(args).PageTextChanges!).Occurrences);
        args.Language = "all";
        Assert.Equal("2", Error(args).Metadata!["foundAtLeast"]);
    }

    [Fact]
    public void Plan_ReportsEveryMissingExactPageName()
    {
        PageTextReplaceArgs args = Args();
        args.Pages = [Page.ToLowerInvariant(), "=Missing/1", Page];

        BridgeError error = Error(args);

        Assert.Equal(ErrorReason.PageNotFound, error.Reason);
        Assert.Equal(new[] { Page.ToLowerInvariant(), "=Missing/1" }.Order(StringComparer.Ordinal), error.Metadata!["missing"].Split('\n'));
        Assert.Equal(3, _reader.Reads.Count);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(50, true)]
    [InlineData(51, false)]
    [InlineData(0, false)]
    public void Plan_PageCount_EnforcesBounds(int count, bool allowed)
    {
        PageTextReplaceArgs args = Args();
        args.Pages = [.. Enumerable.Range(1, count).Select(n => "=Page/" + n.ToString(CultureInfo.InvariantCulture))];
        foreach (string name in args.Pages)
        {
            _reader.Pages[name] = new PageTextSnapshot(name, name + ":id", [Text(1, new TextEntry("en_US", "Motor"))]);
        }

        if (allowed)
        {
            Assert.Equal(count, Plan(args).PageTextChanges!.Count);
        }
        else
        {
            Assert.Equal(ErrorReason.InvalidArgument, Error(args).Reason);
            Assert.Empty(_reader.Reads);
        }
    }

    [Fact]
    public void Plan_DuplicateAndInvalidArguments_ReportsAllViolationsBeforeRead()
    {
        var args = new PageTextReplaceArgs
        {
            Project = " ",
            Pages = [Page, Page, " "],
            Find = string.Empty,
            Replace = new string('x', 501),
            Language = "unknown",
            MaxChanges = 501,
        };

        BridgeError error = Error(args);

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Equal(7, error.Metadata!["violations"].Split('\n').Length);
        Assert.Empty(_reader.Reads);
    }

    [Theory]
    [InlineData("pages", "null")]
    [InlineData("pages", "[null]")]
    [InlineData("find", "null")]
    [InlineData("replace", "null")]
    [InlineData("language", "null")]
    [InlineData("language", "\"\"")]
    [InlineData("maxChanges", "0")]
    [InlineData("maxChanges", "-1")]
    public void Plan_NullOrInvalidFields_RefusesBeforeReading(string field, string value)
    {
        JObject json = (JObject)BridgeJson.FromObject(Args());
        json[field] = JToken.Parse(value);
        var args = (PageTextReplaceArgs)Operation().ParseArgs(json);

        Assert.Equal(ErrorReason.InvalidArgument, Error(args).Reason);
        Assert.Empty(_reader.Reads);
    }

    [Fact]
    public void Plan_LongFindOrReplacement_Enforces500CharacterLimit()
    {
        PageTextReplaceArgs args = Args();
        args.Find = new string('a', 501);
        args.Replace = new string('b', 501);

        Assert.Equal(2, Error(args).Metadata!["violations"].Split('\n').Length);
        Assert.Empty(_reader.Reads);
        args.Find = new string('a', 500);
        args.Replace = new string('b', 500);
        _reader.Pages[Page] = Snapshot(Text(1, new TextEntry("en_US", args.Find)));
        Assert.Equal(args.Replace, Assert.Single(Plan(args).PageTextChanges!).After);
    }

    [Fact]
    public void Plan_ReadOnlyOrUnopenedProject_RefusesBeforeReading()
    {
        _project.ReadOnly = true;
        Assert.Equal(ErrorReason.InvalidArgument, Error(Args()).Reason);
        _project.ReadOnly = false;
        PageTextReplaceArgs args = Args();
        args.Project = "Missing";
        Assert.Equal(ErrorReason.ProjectNotOpen, Error(args).Reason);
        Assert.Empty(_reader.Reads);
    }

    [Fact]
    public void Plan_IncompleteAdapterRead_RefusesInsteadOfReturningPartialMatches()
    {
        PageTextReplaceArgs args = Args();
        args.Pages!.Add("=Z/1");
        _reader.FailingPage = "=Z/1";
        string before = Inventory();

        Assert.Equal(ErrorReason.EplanOperationFailed, Error(args).Reason);
        Assert.Equal(2, _reader.Reads.Count);
        Assert.Equal(before, Inventory());
    }

    [Fact]
    public void Plan_CancelledDuringReads_StopsAtNextPageWithoutChangingInventory()
    {
        var context = new OperationContext("test", write: false, progress: null);
        _reader.OnRead = context.Cancel;
        PageTextReplaceArgs args = Args();
        args.Pages!.Add("=Z/1");
        string before = Inventory();

        BridgeError error = Assert.Throws<BridgeErrorException>(() => context.Run(() => Plan(args))).Error;

        Assert.Equal(ErrorReason.Cancelled, error.Reason);
        Assert.Single(_reader.Reads);
        Assert.Equal(before, Inventory());
    }

    [Fact]
    public void Fingerprint_OrderIsCanonical_AndChangesRemainSorted()
    {
        WritePlan original = Plan(Args("all"));
        PageTextSnapshot page = _reader.Pages[Page];
        _reader.Pages[Page] = Snapshot([.. page.Texts.Reverse().Select(t => new PageFreeText(t.Oid, t.Identifier, [.. t.Entries.Reverse()]))]);

        WritePlan reordered = Plan(Args("all"));

        Assert.Equal(original.Fingerprint, reordered.Fingerprint);
        Assert.Equal(64, original.Fingerprint.Length);
        Assert.Equal(BridgeJson.FromObject(original.PageTextChanges).ToString(), BridgeJson.FromObject(reordered.PageTextChanges).ToString());
    }

    [Fact]
    public void Fingerprint_CoversNonmatchingLanguagesEmptyEntriesObjectsAndPages()
    {
        PageTextSnapshot baseline = Snapshot(Text(1, new TextEntry("en_US", "Motor"), new TextEntry("zh_CN", "电机")));
        string hash = Hash(baseline);

        Assert.NotEqual(hash, Hash(Snapshot(Text(1, new TextEntry("en_US", "Motor"), new TextEntry("zh_CN", "新电机")))));
        Assert.NotEqual(hash, Hash(Snapshot(Text(1, new TextEntry("en_US", "Motor")))));
        Assert.NotEqual(hash, Hash(Snapshot(Text(1, new TextEntry("en_US", "Motor"), new TextEntry("zh_CN", "电机"), new TextEntry("de_DE", string.Empty)))));
        Assert.NotEqual(hash, Hash(Snapshot(Text(1, new TextEntry("en_US", "Motor"), new TextEntry("zh_CN", "电机")), Text(2, new TextEntry("en_US", "Unmatched")))));
        Assert.NotEqual(hash, Hash(Snapshot(Text(1, new TextEntry("en_US", "Motor"), new TextEntry("zh_CN", "电机")), Text(2))));
        Assert.NotEqual(hash, Hash(new PageTextSnapshot(Page, "replacement-page", baseline.Texts)));
        Assert.NotEqual(hash, Hash(Snapshot(new PageFreeText(1, "replacement-text", baseline.Texts[0].Entries))));
        Assert.NotEqual(hash, Hash(Snapshot(Text(2, [.. baseline.Texts[0].Entries]))));
        Assert.NotEqual(hash, Hash(baseline, new PageTextSnapshot("=Empty/1", "empty-page", [])));
        Assert.NotEqual(hash, Hash(new PageTextSnapshot("=Renamed/1", baseline.Identifier, baseline.Texts)));
        Assert.NotEqual(hash, PageTextReplacePlanOperation.StateFingerprint(new OpenProject { Path = @"D:\Scratch\Other" }, [baseline]));
        Assert.Equal(Hash(baseline, new PageTextSnapshot("=Empty/1", "empty-page", [])), Hash(new PageTextSnapshot("=Empty/1", "empty-page", []), baseline));
    }

    [Fact]
    public void Fingerprint_RawDelimitersAndControlCharacters_AreUnambiguous()
    {
        string hash = Hash(Snapshot(Text(1, new TextEntry("en_US", "a\nb@c;\t\u202E😀"))));

        Assert.NotEqual(hash, Hash(Snapshot(Text(1, new TextEntry("en_US", "a"), new TextEntry("b", "c;\t\u202E😀")))));
        Assert.NotEqual(hash, Hash(Snapshot(Text(1, new TextEntry("en_US", "a\nb@c;\t😀")))));
        Assert.NotEqual(Hash(Snapshot(Text(1, new TextEntry("en_US", new string('x', 501))))), Hash(Snapshot(Text(1, new TextEntry("en_US", new string('x', 500))))));
    }

    [Fact]
    public void Plan_LongResults_StayCompleteForFutureHostShaping()
    {
        string text = string.Concat(Enumerable.Repeat("Motor\n\u202E@;", 200));
        _reader.Pages[Page] = Snapshot(Text(1, new TextEntry("en_US", text)));

        PageTextChange change = Assert.Single(Plan().PageTextChanges!);

        Assert.Equal(text, change.Before);
        Assert.Equal(text.Replace("Motor", "Drive", StringComparison.Ordinal), change.After);
        Assert.Equal(200, change.Occurrences);
    }

    [Fact]
    public void Plan_WireRoundTrip_PreservesDefaultsAndStructuredMatches()
    {
        PageTextReplacePlanOperation operation = Operation();
        JObject wire = (JObject)BridgeJson.FromObject(Args());
        wire.Remove("caseSensitive");
        wire.Remove("maxChanges");
        var args = (PageTextReplaceArgs)operation.ParseArgs(wire);

        var plan = (WritePlan)operation.Execute(args)!;
        WritePlan decoded = BridgeJson.FromObject(plan).ToObject<WritePlan>(BridgeJson.Serializer)!;

        Assert.Equal(OperationNames.PageTextReplacePlan, operation.Name);
        Assert.True(args.CaseSensitive);
        Assert.Equal(500, args.MaxChanges);
        Assert.Equal(plan.Fingerprint, decoded.Fingerprint);
        Assert.Equal(["en_US", "en_US"], decoded.PageTextChanges!.Select(c => c.Language));
        Assert.Equal([1, 2], decoded.PageTextChanges!.Select(c => c.Occurrences));
    }

    private PageTextReplacePlanOperation Operation() => new(new FakeCatalog(_project), _reader);

    private WritePlan Plan(PageTextReplaceArgs? args = null) => (WritePlan)Operation().Execute(args ?? Args())!;

    private BridgeError Error(PageTextReplaceArgs args) => Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;

    private static PageTextReplaceArgs Args(string language = "en_US") =>
        new() { Project = "Line1", Pages = [Page], Find = "Motor", Replace = "Drive", Language = language };

    private static PageTextSnapshot Snapshot(params PageFreeText[] texts) => new(Page, "page:1", texts);

    private static PageFreeText Text(long oid, params TextEntry[] entries) => new(oid, "text:" + oid.ToString(CultureInfo.InvariantCulture), entries);

    private string Hash(params PageTextSnapshot[] pages) => PageTextReplacePlanOperation.StateFingerprint(_project, pages);

    private string Inventory() => BridgeJson.FromObject(_reader.Pages).ToString();
}
