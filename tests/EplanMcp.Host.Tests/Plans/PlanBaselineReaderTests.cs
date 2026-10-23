using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Tests.Bridge;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Host.Tests.Plans;

/// <summary>Real Host client, both serializers and fake Bridge operation over a scripted channel; never discovers EPLAN.</summary>
[Trait("Size", "Medium")]
public sealed class PlanBaselineReaderTests
{
    private static readonly PlanProject _project = new("Line1", @"D:\Projects\Line1", "sample@1");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Names_IncludeUnplacedFunctionsAndSuffixes_FromEveryListPageOnBothReads()
    {
        await using var fake = new FakeSnapshotBridge((_, _) => Data(Pages()));
        fake.NameAnswer = (args, _) => new()
        {
            Project = _project.Name,
            Total = 501,
            Items = [.. Enumerable.Range(args.Offset, Math.Min(500, 501 - args.Offset))
                .Select(i => new DeviceListItem { Tag = "+A1-K" + i + ":1", Page = null })],
        };
        PlanBaseline baseline = Assert.IsType<PlanBaseline>((await fake.Reader.ReadAsync(_project, null, Token)).Baseline);
        Assert.Equal([0, 500, 0, 500], fake.NameCalls.Select(a => a.Offset));
        Assert.All(fake.NameCalls, a => { Assert.True(a.IncludeAuxiliary); Assert.Null(a.Page); Assert.Equal(_project.Path, a.Project); });
        Assert.Contains(new PlanOccupiedName("function", "+A1-K500:1"), baseline.Names);
        Assert.Throws<ArgumentException>(() => PlanPreconditions.Build(baseline, PlanScopeRules.Whole(baseline.Snapshot), [new("function", "+A1-K500")]));
    }

    [Fact]
    public async Task Names_UnplacedFunctionAddedBetweenReads_IsStale()
    {
        await using var fake = new FakeSnapshotBridge((_, _) => Data(Pages()));
        fake.NameAnswer = (_, call) => new()
        {
            Project = _project.Name,
            Total = 1,
            Items = [new() { Tag = call == 1 ? "+A1-K1" : "+A1-K2", Page = null }]
        };
        PlanBaselineRead result = await fake.Reader.ReadAsync(_project, null, Token);
        Assert.Equal(ErrorReason.PlanStale, result.Reason);
        Assert.Equal("names", Assert.Single(result.Conflicts).Scope);
    }

    [Fact]
    public async Task Names_TruncatedList_DoesNotProveAbsence()
    {
        await using var fake = new FakeSnapshotBridge((_, _) => Data(Pages()));
        fake.NameAnswer = (_, _) => new() { Project = _project.Name, Total = 1 };
        PlanBaselineRead result = await fake.Reader.ReadAsync(_project, null, Token);
        Assert.Null(result.Baseline);
        Assert.Equal("names:slice", Assert.Single(result.Conflicts).Scope);
    }

    [Fact]
    public async Task WholeProject_StableDoubleRead_ReturnsCoverageHashesAndNoRuntimeIdentifiers()
    {
        await using var fake = new FakeSnapshotBridge((_, _) => Data(Pages()));
        PlanBaselineRead result = await fake.Reader.ReadAsync(_project, null, Token);
        PlanBaseline baseline = Assert.IsType<PlanBaseline>(result.Baseline);
        Assert.Null(result.Reason);
        Assert.Empty(result.Conflicts);
        Assert.Equal(2, fake.Calls.Count);
        Assert.Equal(2, baseline.Snapshot.Pages.Length);
        Assert.Equal(4, baseline.Snapshot.Members.Count);
        Assert.Equal(2, baseline.Snapshot.PageHashes.Count);
        Assert.Equal(3, baseline.Snapshot.SkippedConnections);
        Assert.Equal(SnapshotCanonical.SnapshotHash("standard", Pages()), baseline.Snapshot.SnapshotHash);
        Assert.Equal(baseline.Snapshot.SnapshotHash, baseline.Snapshot.ComposedHash);
        Assert.Contains(new PlanOccupiedName("function", "+A10-K2"), baseline.Names);
        string json = JsonSerializer.Serialize(baseline);
        foreach (string forbidden in new[] { "ToStringIdentifier", "DatabaseIdentifier", "Fingerprint", "53/17/", "53/4/" })
        {
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task MultipleChunks_DoubleRead_ComposesCoverageWithoutWholeProjectHash()
    {
        await using var fake = new FakeSnapshotBridge((args, _) => Chunk(args));
        PlanBaselineRead result = await fake.Reader.ReadAsync(_project, null, Token);
        PlanSnapshot snapshot = Assert.IsType<PlanBaseline>(result.Baseline).Snapshot;
        Assert.Null(snapshot.SnapshotHash);
        Assert.Equal(SnapshotCanonical.SnapshotHash("standard", Pages()), snapshot.ComposedHash);
        Assert.Equal(new long?[] { null, 7, null, 7 }, fake.Calls.Select(a => a.AfterPage));
        Assert.All(fake.Calls, a => Assert.Equal("standard", a.Profile));
        Assert.All(fake.Calls, a => Assert.False(a.OpenReadOnly));
        Assert.All(fake.Calls, a => Assert.Equal(_project.Path, a.Project));
    }

    [Fact]
    public async Task LocalScope_ReadsProjectNameDomainTwice_AndKeepsPartialCoverage()
    {
        await using var fake = new FakeSnapshotBridge((args, _) => args.Pages is null ? Chunk(args) : Data(Pages().Take(1), whole: false));
        PlanBaselineRead result = await fake.Reader.ReadAsync(_project, ["+A1/1", "+A1/1"], Token);
        PlanBaseline baseline = Assert.IsType<PlanBaseline>(result.Baseline);
        Assert.Single(baseline.Snapshot.Pages);
        Assert.Single(baseline.Snapshot.PageHashes);
        Assert.Null(baseline.Snapshot.SnapshotHash);
        Assert.NotEqual(PlanSnapshotHashes.Compose(Pages()), baseline.Snapshot.ComposedHash);
        Assert.Contains(new PlanOccupiedName("page", "+A10/2"), baseline.Names);
        Assert.Equal(6, fake.Calls.Count);
        Assert.Equal(2, fake.Calls.Count(c => c.Pages is not null));
    }

    [Fact]
    public async Task MissingPages_StopsWithStaleConflict_WithoutRetry()
    {
        await using var fake = new FakeSnapshotBridge((_, _) =>
        {
            ProjectSnapshotData data = Data([], whole: false);
            data.MissingPages = ["+A1/absent"];
            return data;
        });
        PlanBaselineRead result = await fake.Reader.ReadAsync(_project, ["+A1/absent"], Token);
        Assert.Null(result.Baseline);
        Assert.Equal(ErrorReason.PlanStale, result.Reason);
        Assert.Contains(result.Conflicts, c => c.Reason == "missing" && c.Expected == "+A1/absent");
        Assert.Single(fake.Calls);
    }

    [Fact]
    public async Task ChangeBetweenReads_ListsChangedAddedRemovedMembers_WithoutRetry()
    {
        await using var fake = new FakeSnapshotBridge((_, call) => Data(Pages(changed: call == 2)));
        PlanBaselineRead result = await fake.Reader.ReadAsync(_project, null, Token);
        Assert.Null(result.Baseline);
        Assert.Equal(ErrorReason.PlanStale, result.Reason);
        Assert.Contains(result.Conflicts, c => c.Scope == "pages:page/4/7" && c.Reason == "changed");
        Assert.Contains(result.Conflicts, c => c.Scope == "members:function/17/22" && c.Reason == "removed");
        Assert.Contains(result.Conflicts, c => c.Scope == "members:function/17/23" && c.Reason == "added");
        Assert.Equal(2, fake.Calls.Count);
    }

    [Fact]
    public async Task NameAddedOutsideScope_ConflictsEvenWhenLocalPageIsUnchanged()
    {
        await using var fake = new FakeSnapshotBridge((args, call) =>
            Data(args.Pages is null ? Pages(renamedOutside: call == 4) : Pages().Take(1), whole: args.Pages is null));
        PlanBaselineRead result = await fake.Reader.ReadAsync(_project, ["+A1/1"], Token);
        Assert.Null(result.Baseline);
        Assert.Equal(ErrorReason.PlanStale, result.Reason);
        Assert.Equal("names", Assert.Single(result.Conflicts).Scope);
        Assert.Equal(4, fake.Calls.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PageSetChangesBetweenReads_ReportsAddedOrRemovedPage(bool added)
    {
        await using var fake = new FakeSnapshotBridge((_, call) => Data((call == 2) == added ? Pages() : Pages().Take(1)));
        PlanBaselineRead result = await fake.Reader.ReadAsync(_project, null, Token);
        Assert.Null(result.Baseline);
        Assert.Contains(result.Conflicts, c => c.Scope == "pages:page/4/9" && c.Reason == (added ? "added" : "removed"));
        Assert.Contains(result.Conflicts, c => c.Scope == "names");
        Assert.Equal(2, fake.Calls.Count);
    }

    [Fact]
    public async Task PropertyChangesWithSameMembers_StillConflictsOnPageAndObjectHashes()
    {
        await using var fake = new FakeSnapshotBridge((_, call) =>
        {
            SnapshotPage[] pages = Pages();
            if (call == 2)
            {
                SnapshotObject function = pages[0].Objects[1];
                function.Values = [new SnapshotValue { Id = 20027, Value = "24 V" }];
                function.Hash = SnapshotCanonical.Sha256(SnapshotCanonical.Text(function));
                pages[0].Hash = SnapshotCanonical.PageHash("standard", pages[0].Objects);
            }

            return Data(pages);
        });
        PlanBaselineRead result = await fake.Reader.ReadAsync(_project, null, Token);
        Assert.Null(result.Baseline);
        Assert.Contains(result.Conflicts, c => c.Scope == "members:function/17/22" && c.Reason == "changed");
        Assert.Contains(result.Conflicts, c => c.Scope == "pages:page/4/7" && c.Reason == "changed");
        Assert.DoesNotContain(result.Conflicts, c => c.Scope == "names");
    }

    [Fact]
    public async Task ProjectCopyOnSecondRead_IsStaleEvenWithSameGuidAndHashes()
    {
        await using var fake = new FakeSnapshotBridge((_, call) =>
        {
            ProjectSnapshotData data = Data(Pages());
            data.ProjectPath = call == 2 ? @"D:\Copy\Line1" : data.ProjectPath;
            return data;
        });
        PlanBaselineRead result = await fake.Reader.ReadAsync(_project, null, Token);
        Assert.Null(result.Baseline);
        Assert.Equal("projectPath", Assert.Single(result.Conflicts).Scope);
        Assert.Equal(2, fake.Calls.Count);
    }

    [Fact]
    public async Task MissingProjectPath_IsStaleInsteadOfResolvingAgainstTheWorkingDirectory()
    {
        await using var fake = new FakeSnapshotBridge((_, _) =>
        {
            ProjectSnapshotData data = Data(Pages());
            data.ProjectPath = string.Empty;
            return data;
        });
        PlanBaselineRead result = await fake.Reader.ReadAsync(_project, null, Token);
        Assert.Null(result.Baseline);
        Assert.Equal("projectPath", Assert.Single(result.Conflicts).Scope);
        Assert.Single(fake.Calls);
    }

    [Fact]
    public async Task CancelledBeforeReading_DoesNotCallBridge()
    {
        await using var fake = new FakeSnapshotBridge((_, _) => Data(Pages()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fake.Reader.ReadAsync(_project, null, new CancellationToken(canceled: true)));
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task BridgeFailure_PropagatesWithoutRetry()
    {
        await using var fake = new FakeSnapshotBridge((_, _) => throw new BridgeErrorException(ErrorReason.ProjectNotOpen, "Open the project first."));
        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => fake.Reader.ReadAsync(_project, null, Token));
        Assert.Equal(ErrorReason.ProjectNotOpen, error.Error.Reason);
        Assert.Single(fake.Calls);
    }

    [Theory]
    [InlineData("format", 1)]
    [InlineData("profile", 1)]
    [InlineData("projectPath", 2)]
    [InlineData("projectGuid", 2)]
    [InlineData("format", 2)]
    [InlineData("profile", 2)]
    public async Task IncompatibleChunk_RejectsIdentityAndFormatChanges(string field, int at)
    {
        await using var fake = new FakeSnapshotBridge((args, call) =>
        {
            ProjectSnapshotData data = Chunk(args);
            if (call == at)
            {
                switch (field)
                {
                    case "format":
                        data.Format = "eplanmcp.snapshot/2";
                        break;
                    case "profile":
                        data.Profile = "other";
                        break;
                    case "projectPath":
                        data.ProjectPath = @"D:\Copy\Line1";
                        break;
                    case "projectGuid":
                        data.ProjectGuid = "different@2";
                        break;
                }
            }

            return data;
        });
        PlanBaselineRead result = await fake.Reader.ReadAsync(_project, null, Token);
        Assert.Null(result.Baseline);
        Assert.Equal(ErrorReason.PlanStale, result.Reason);
        Assert.Contains(result.Conflicts, c => c.Scope == field);
        Assert.Equal(at, fake.Calls.Count);
    }

    [Theory]
    [InlineData("cursor")]
    [InlineData("empty")]
    [InlineData("count")]
    [InlineData("overlap")]
    [InlineData("hash")]
    [InlineData("partial_hash")]
    [InlineData("final_count")]
    [InlineData("page_object")]
    public async Task BrokenChunk_RejectsIncompleteOrInconsistentCoverage(string fault)
    {
        await using var fake = new FakeSnapshotBridge((args, call) =>
        {
            ProjectSnapshotData data = Chunk(args);
            switch (fault)
            {
                case "cursor":
                    data.NextAfterPage = 8;
                    break;
                case "empty":
                    data.Pages = [];
                    break;
                case "count":
                    data.PageCount = call == 1 ? 2 : 3;
                    break;
                case "overlap" when call == 2:
                    data.Pages = [Pages()[0]];
                    break;
                case "hash":
                    data.Pages[0].Hash = new string('0', 64);
                    break;
                case "partial_hash":
                    data.SnapshotHash = PlanSnapshotHashes.Compose(data.Pages);
                    break;
                case "final_count":
                    data.PageCount = 3;
                    break;
                case "page_object":
                    data.Pages[0].Objects.RemoveAt(0);
                    break;
            }

            return data;
        });
        PlanBaselineRead result = await fake.Reader.ReadAsync(_project, null, Token);
        Assert.Null(result.Baseline);
        Assert.Equal(ErrorReason.PlanStale, result.Reason);
        Assert.NotEmpty(result.Conflicts);
        Assert.InRange(fake.Calls.Count, 1, 2);
    }

    [Fact]
    public async Task ZeroPageProject_IsStableAndHasAnEmptyNameSet()
    {
        await using var fake = new FakeSnapshotBridge((_, _) => Data([]));
        PlanBaselineRead result = await fake.Reader.ReadAsync(_project, null, Token);
        PlanBaseline baseline = Assert.IsType<PlanBaseline>(result.Baseline);
        Assert.Empty(baseline.Names);
        Assert.Empty(baseline.Snapshot.Members);
        Assert.Empty(baseline.Snapshot.Pages);
        Assert.NotNull(baseline.Snapshot.SnapshotHash);
    }

    private static SnapshotPage[] Pages(bool changed = false, bool renamedOutside = false) =>
        [Page(7, "+A1/1", changed ? 23 : 22, "+A1-K1"), Page(9, "+A10/2", 24, renamedOutside ? "+A10-K3" : "+A10-K2")];

    private static SnapshotPage Page(long oid, string name, long functionOid, string tag) => SnapshotCanonical.Page("standard",
        new SnapshotSourceObject { Kind = "page", Identifier = $"53/4/{oid}/0", Oid = oid, Name = name, PageType = "Circuit" },
        [new SnapshotSourceObject { Kind = "function", Identifier = $"53/17/{functionOid}/0", Oid = functionOid, Name = tag, Page = name }],
        SnapshotCanonical.Registered("standard")!);

    private static ProjectSnapshotData Data(IEnumerable<SnapshotPage> pages, bool whole = true)
    {
        var all = pages.ToList();
        return new()
        {
            Project = _project.Name,
            ProjectPath = _project.Path,
            ProjectGuid = _project.Guid,
            Profile = "standard",
            Format = SnapshotProfiles.Format,
            PageCount = all.Count,
            Pages = all,
            Complete = true,
            SnapshotHash = whole ? SnapshotCanonical.SnapshotHash("standard", all) : null,
            SkippedConnections = 3,
        };
    }

    private static ProjectSnapshotData Chunk(ProjectSnapshotArgs args)
    {
        ProjectSnapshotData data = Data(args.AfterPage is null ? Pages().Take(1) : Pages().Skip(1), whole: false);
        data.PageCount = 2;
        data.Complete = args.AfterPage is not null;
        data.NextAfterPage = data.Complete ? null : 7;
        return data;
    }

    private sealed class FakeSnapshotBridge : IAsyncDisposable
    {
        private readonly BridgeClient _client;
        public List<ProjectSnapshotArgs> Calls { get; } = [];
        public PlanBaselineReader Reader { get; }
        public Func<DeviceListArgs, int, ListData<DeviceListItem>> NameAnswer { get; set; } = (_, _) => new() { Project = _project.Name };
        public List<DeviceListArgs> NameCalls { get; } = [];

        public FakeSnapshotBridge(Func<ProjectSnapshotArgs, int, ProjectSnapshotData> answer)
        {
            var operation = new SnapshotOperation(args => { Calls.Add(args); return answer(args, Calls.Count); });
            var endpoint = new BridgeEndpoint("EplanMcpTest.Baseline", 123);
            var discovery = new FakeDiscovery();
            discovery.Endpoints.Add(endpoint);
            var connector = new FakeConnector(e => new FakeChannel(e, (method, envelope, _) =>
            {
                Assert.Equal(BridgeProtocol.InvokeMethod, method);
                if (envelope!["operation"]!.GetValue<string>() == OperationNames.DeviceList)
                {
                    var namesArgs = System.Text.Json.JsonSerializer.Deserialize<DeviceListArgs>(envelope["args"]!.ToJsonString(), HostJson.Options)!;
                    NameCalls.Add(namesArgs);
                    return Task.FromResult(JsonNode.Parse(new JObject { ["data"] = JToken.FromObject(NameAnswer(namesArgs, NameCalls.Count)) }.ToString()));
                }
                Assert.Equal(OperationNames.ProjectSnapshot, envelope!["operation"]!.GetValue<string>());
                object args = operation.ParseArgs(JToken.Parse(envelope["args"]!.ToJsonString()));
                try
                {
                    object? data = operation.Execute(args);
                    JsonNode? response = JsonNode.Parse(new JObject { ["data"] = JToken.FromObject(data!) }.ToString());
                    return Task.FromResult(response);
                }
                catch (BridgeErrorException ex)
                {
                    throw FakeChannel.BusinessError(ex.Error.Reason, ex.Error.Message);
                }
            })
            {
                Handshake = (_, _, _) => Task.FromResult(FakeChannel.CompatibleHandshake(e.ProcessId, operations: [OperationNames.ProjectSnapshot, OperationNames.DeviceList])),
            });
            _client = new BridgeClient(discovery, connector, NullLogger<BridgeClient>.Instance);
            Reader = new(_client);
        }

        public ValueTask DisposeAsync() => _client.DisposeAsync();
    }

    private sealed class SnapshotOperation(Func<ProjectSnapshotArgs, ProjectSnapshotData> answer) : Operation<ProjectSnapshotArgs, ProjectSnapshotData>
    {
        public override string Name => OperationNames.ProjectSnapshot;
        protected override ProjectSnapshotData Execute(ProjectSnapshotArgs args) => answer(args);
    }

}
