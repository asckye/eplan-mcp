using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Tests;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlanStepGuardOperationTests : IDisposable
{
    private readonly PlanGuardReader _reader = new();
    public void Dispose() => _reader.Dispose();
    private PlanStepGuardOperation Operation => new(_reader, _reader, _reader, _reader, () => _reader.Milliseconds);
    private string Run(PlanStepGuardArgs args) => Assert.IsType<string>(Operation.Execute(args));

    [Fact]
    public void Guard_AllSixKindsMatch_ReadsOnceAndNeverWritesOrUsesHints()
    {
        Assert.Equal("ok", Run(_reader.Args(all: true)));
        Assert.Equal((1, 1, 1), (_reader.Opens, _reader.Reads, _reader.Disposals));
        Assert.False(typeof(IWriteOperation).IsAssignableFrom(typeof(PlanStepGuardOperation)));
    }

    [Fact]
    public void Guard_ChangedDescription_ListsEveryFailedCheckAsCanonicalJson()
    {
        PlanStepGuardArgs args = _reader.Args(all: true);
        _reader.Content[1][0].Values = [new(11011, null, "en_US@Changed;")];
        BridgeError error = Failure(args);
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted, false), (error.Reason, error.Execution, error.Retryable));
        Assert.Equal(["conflicts"], error.Metadata!.Keys);
        string json = error.Metadata["conflicts"];
        var conflicts = JArray.Parse(json);
        Assert.Equal(["members", "objectHash", "pageHash"], conflicts.Select(c => (string)c["reason"]!));
        Assert.Equal(json, PlanGuardScopeRules.Canonical(conflicts).ToString(Newtonsoft.Json.Formatting.None));
        Assert.All(conflicts, c =>
        {
            Assert.True(c["locator"] is not null ^ c["scope"] is not null);
            Assert.NotNull(c["expected"]);
            Assert.NotNull(c["actual"]);
            Assert.Equal(4, ((JObject)c).Count);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Guard_AddedOrRemovedMember_ConflictsEvenWithoutIndividualObjectChecks(bool add)
    {
        PlanStepGuardArgs args = _reader.Args();
        if (add)
        {
            _reader.Content[1].Add(new() { Kind = "function", Identifier = "28/17/3/0", Oid = 3, Page = "+A1/1", Name = "+A1-K2" });
        }
        else
        {
            _reader.Content[1].RemoveAt(1);
        }
        JToken conflict = Assert.Single(Conflicts(args));
        Assert.Equal("members", (string?)conflict["reason"]);
        Assert.Equal(add ? 3 : 1, (int)conflict["actual"]!["count"]!);
    }

    [Theory]
    [InlineData("page", "+a1/9")]
    [InlineData("function", "+a1-k9")]
    [InlineData("function", "+A1-K9:1")]
    public void Guard_NameNowPresentIncludingUnplacedTerminals_ReportsAbsent(string kind, string name)
    {
        PlanStepGuardArgs args = _reader.Args(all: true);
        if (kind == "page")
        {
            _reader.Content[9] = [new() { Kind = "page", Identifier = "28/4/9/0", Oid = 9, Name = name }];
        }
        else
        {
            _reader.Names.Add(new() { Type = 17, Oid = 9, Name = name, Page = null });
        }
        JToken conflict = Assert.Single(Conflicts(args));
        Assert.Equal("absent", (string?)conflict["reason"]);
        Assert.False((bool)conflict["expected"]!);
        Assert.True((bool)conflict["actual"]!);
    }

    [Theory]
    [InlineData("resource")]
    [InlineData("revision")]
    [InlineData("digest")]
    public void Guard_ExternalEvidenceMismatch_ReportsOnlyThatPrecondition(string change)
    {
        PlanStepGuardArgs args = _reader.Args(all: true);
        if (change == "resource")
        {
            args.ResourceHashes = [new string('c', 64)];
        }
        else if (change == "revision")
        {
            args.BindingRevision++;
        }
        else
        {
            args.LockDigest = new string('c', 64);
        }
        JToken conflict = Assert.Single(Conflicts(args));
        Assert.Equal(change == "resource" ? "resource" : "binding", (string?)conflict["reason"]);
    }

    [Theory]
    [InlineData("project")]
    [InlineData("not_found")]
    [InlineData("type")]
    [InlineData("name")]
    [InlineData("page")]
    [InlineData("category")]
    [InlineData("position")]
    public void Guard_LocatorChecks_MatchObjectResolve(string reason)
    {
        PlanStepGuardArgs args = _reader.Args();
        SnapshotObject obj = _reader.Objects()[1];
        ObjectLocator locator = PlanGuardReader.Locator(obj);
        args.Preconditions.Add(new() { Tag = "objectHash", Locator = locator, Hash = obj.Hash });
        switch (reason)
        {
            case "project":
                locator.Project = @"D:\Projects\Copy";
                break;
            case "not_found":
                locator.Oid = 99;
                break;
            case "type":
                locator.Type = 18;
                break;
            case "name":
                locator.Name = "Other";
                break;
            case "page":
                locator.Page = "+A1/9";
                break;
            case "category":
                locator.Category = "Other";
                break;
            case "position":
                locator.Pos = "11,20";
                break;
        }
        Assert.Equal(reason, (string?)Assert.Single(Conflicts(args))["reason"]);
    }

    [Fact]
    public void Guard_AtTenSeconds_StopsBeforeAnotherPageAndDisposesSource()
    {
        PlanStepGuardArgs args = _reader.Args();
        _reader.PerRead = 10_000;
        BridgeError error = Failure(args);
        Assert.Equal((ErrorReason.Timeout, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Equal((1, 1), (_reader.Reads, _reader.Disposals));
    }

    [Fact]
    public void Guard_FinalComparisonOverBudget_TimeoutOverridesConflicts()
    {
        PlanStepGuardArgs args = _reader.Args();
        int calls = 0;
        var operation = new PlanStepGuardOperation(_reader, _reader, _reader, _reader, () => ++calls < 8 ? 0 : 10_000);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => operation.Execute(args)).Error;
        Assert.Equal((ErrorReason.Timeout, ExecutionState.NotStarted), (error.Reason, error.Execution));
    }

    [Theory]
    [InlineData("tag")]
    [InlineData("extra")]
    [InlineData("missingMembers")]
    [InlineData("badHash")]
    [InlineData("count")]
    [InlineData("pages")]
    [InlineData("locators")]
    [InlineData("structure")]
    [InlineData("position")]
    public void Guard_InvalidClosedConditionsAndBounds_AreRejectedBeforeReading(string invalid)
    {
        PlanStepGuardArgs args = _reader.Args();
        switch (invalid)
        {
            case "tag":
                args.Preconditions.Add(new() { Tag = "code" });
                break;
            case "extra":
                args.Preconditions[0].Name = "Unexpected";
                break;
            case "missingMembers":
                args.Preconditions.Clear();
                break;
            case "badHash":
                args.Preconditions[0].Hash = "wrong";
                break;
            case "count":
                args.Preconditions[0].Count = 10_001;
                break;
            case "pages":
                args.ReadScope.Pages = [.. Enumerable.Range(1, 51).Select(i => "+A1/" + i)];
                break;
            case "locators":
                args.ReadScope.Locators = [.. Enumerable.Range(1, 201).Select(i => new ObjectLocator { Project = PlanGuardReader.Project, Kind = "function", Type = 17, Oid = i })];
                break;
            case "structure":
                args.ReadScope.Structures = ["+A1++Bad"];
                break;
            case "position":
                args.ReadScope.Locators = [new() { Project = PlanGuardReader.Project, Kind = "function", Type = 17, Oid = 2, Pos = "wrong" }];
                break;
        }
        Assert.Equal(ErrorReason.InvalidArgument, Failure(args).Reason);
        Assert.Equal(0, _reader.Opens);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("preconditions[0].unknown")]
    [InlineData("readScope.unknown")]
    public void Guard_UnknownWireFields_AreRejected(string path)
    {
        JObject json = JObject.FromObject(_reader.Args(), EplanMcp.Bridge.Core.Protocol.BridgeJson.Serializer);
        int at = path.LastIndexOf('.');
        JObject target = at < 0 ? json : (JObject)json.SelectToken(path[..at])!;
        target[at < 0 ? path : path[(at + 1)..]] = "ignored";
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Operation.ParseArgs(json)).Error.Reason);
    }

    [Fact]
    public void Guard_ObjectsOverLimit_AreRejectedWithoutPartialSuccess()
    {
        PlanStepGuardArgs args = _reader.Args();
        _reader.Content[1].AddRange(Enumerable.Range(3, 9999).Select(i => new SnapshotSourceObject
        {
            Kind = "function",
            Identifier = $"28/17/{i}/0",
            Oid = i,
            Page = "+A1/1",
        }));
        Assert.Equal(ErrorReason.InvalidArgument, Failure(args).Reason);
        Assert.Equal(1, _reader.Disposals);
    }

    [Fact]
    public void Guard_StructurePrefix_UsesWholeSegmentsAndIntersection()
    {
        PlanStepGuardArgs args = _reader.Args();
        args.ReadScope.Pages.Clear();
        args.ReadScope.Structures = ["+A1"];
        _reader.Content[10] = [new() { Kind = "page", Identifier = "28/4/10/0", Oid = 10, Name = "+A10/1" }];
        Assert.Equal("ok", Run(args));
        Assert.Equal(1, _reader.Reads);
    }

    [Theory]
    [InlineData(@"D:\Projects\Line1")]
    [InlineData(@"D:\Projects\工程+<&'`😀")]
    [InlineData("D:\\Projects\\Control\u007f")]
    [InlineData(@"D:\Projects\a\..\b.elk")]
    public void Members_MatchesIndependentP06FormulaIncludingUnicodeAndEscapes(string project)
    {
        Assert.Equal(PlanGuardReader.MemberHash(project, _reader.Objects()), PlanGuardScopeRules.Members(project, _reader.Objects()));
    }

    [Fact]
    public void Guard_ReadCall_UsesExactlyOneDispatcherInvocationAndNoOutcomeRecord()
    {
        var dispatcher = new InlineDispatcher();
        var router = new OperationRouter([Operation], dispatcher, null, new ListLog(), TimeSpan.FromSeconds(10));
        Assert.Equal("ok", (string?)router.Invoke(OperationNames.PlanStepGuard,
            EplanMcp.Bridge.Core.Protocol.BridgeJson.FromObject(_reader.Args(all: true)), "guard"));
        Assert.Equal(1, dispatcher.Calls);
    }

    private BridgeError Failure(PlanStepGuardArgs args) => Assert.Throws<BridgeErrorException>(() => Run(args)).Error;
    private JArray Conflicts(PlanStepGuardArgs args) => JArray.Parse(Failure(args).Metadata!["conflicts"]);
}
