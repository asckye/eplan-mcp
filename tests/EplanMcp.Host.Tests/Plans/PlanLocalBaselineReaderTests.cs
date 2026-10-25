using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Tests.Tools;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Medium")]
public sealed class PlanLocalBaselineReaderTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("page.create", 0)]
    [InlineData("device.update", 2)]
    [InlineData("macro.insert", 1)]
    public async Task Write_LocalDoubleRead_NeverRequestsWholeStandardSnapshot_WithOverTenThousandNames(string op, int expectedPages)
    {
        var fake = new LocalBridge(op == "macro.insert" ? PlanScopeTestData.Baseline([]) : null);
        fake.ExtraNames = [.. Enumerable.Range(0, 10_001).Select(i => new PlanNameEntry { Kind = "function", Name = "+REMOTE-K" + i })];
        int compiled = 0;
        PlanBaselineRead result = await fake.ReadAsync([Request(op)], (baseline, impacts) =>
        {
            compiled++;
            Assert.Equal(expectedPages, baseline.Snapshot.Pages.Length);
            Assert.Null(baseline.Snapshot.SnapshotHash);
            Assert.True(baseline.Names.Length > 10_000);
            Assert.Equal(fake.Names().NamesHash, baseline.NamesHash);
            Assert.Equal(PlanSnapshotHashes.Compose(baseline.Snapshot.Pages), baseline.Snapshot.ComposedHash);
            PlanImpact impact = PlanLocalScopeRules.Build(baseline, Assert.Single(impacts), PlanLocalScopeRulesTests.Plan(baseline,
                op == "device.update" ? "+A1-K9" : null), op == "macro.insert" ? PlanLocalScopeRulesTests.Macro(1) : null);
            Assert.Equal(expectedPages, impact.ReadScope.Pages.Length);
            return Task.CompletedTask;
        });
        Assert.NotNull(result.Baseline);
        Assert.Null(result.Reason);
        Assert.Equal(1, compiled);
        Assert.Equal(3, fake.NameReads);
        Assert.Equal(expectedPages == 0 ? 0 : 2, fake.Snapshots.Count);
        Assert.All(fake.Snapshots, a =>
        {
            Assert.Equal(SnapshotProfiles.StandardName, a.Profile);
            Assert.Equal(expectedPages, a.Pages!.Count);
            Assert.DoesNotContain("+A10/1", a.Pages);
        });
        Assert.DoesNotContain(fake.Client.Calls, c => c.Operation == OperationNames.DeviceList || c.Operation.EndsWith(".execute", StringComparison.Ordinal));
        string json = JsonSerializer.Serialize(result.Baseline, PlanJson.Options);
        Assert.Equal(json, JsonSerializer.Serialize(PlanJson.Read<PlanBaseline>(json), PlanJson.Options));
    }

    [Fact]
    public async Task PageCreate_ZeroPageProject_UsesEmptySnapshotGoldenAndCompleteUnplacedNames()
    {
        var fake = new LocalBridge(PlanScopeTestData.Baseline([], []));
        fake.ExtraNames = [new() { Kind = "function", Name = "+A1-UNPLACED" }];
        PlanBaseline baseline = Assert.IsType<PlanBaseline>((await fake.ReadAsync([Request("page.create")])).Baseline);
        Assert.Empty(fake.Snapshots);
        Assert.Empty(baseline.Snapshot.Pages);
        Assert.Empty(baseline.Snapshot.PageHashes);
        Assert.Empty(baseline.Snapshot.Members);
        Assert.Null(baseline.Snapshot.SnapshotHash);
        Assert.Equal("0a2eca8b72ce6c1f0dabb2a706b55481c9a6725faaa9f859b55943d947f9e7f5", baseline.Snapshot.ComposedHash);
        Assert.Equal("da024fe81b16951b25f6dd30b21ec6d5f058f1eebedbb7f9638c859da255dc91", baseline.Snapshot.MembersHash);
        Assert.Equal([new PlanOccupiedName("function", "+A1-UNPLACED")], baseline.Names);
    }

    [Fact]
    public async Task MissingPageOnSecondRead_IsStale_WithoutRetryOrSecondCompilation()
    {
        var fake = new LocalBridge();
        fake.SnapshotChange = (data, call) =>
        {
            if (call == 2)
            {
                data.MissingPages = ["+A1/2"];
                data.Pages.RemoveAll(p => p.Name == "+A1/2");
            }
        };
        int compiled = 0;
        PlanBaselineRead result = await fake.ReadAsync([Request("device.update")], (_, _) =>
        {
            compiled++;
            return Task.CompletedTask;
        });
        Assert.Equal(ErrorReason.PlanStale, result.Reason);
        Assert.Null(result.Baseline);
        Assert.Contains(result.Conflicts, c => c.Reason == "missing");
        Assert.Equal(2, fake.Snapshots.Count);
        Assert.Equal(1, compiled);
    }

    [Theory]
    [InlineData("page.create")]
    [InlineData("device.update")]
    [InlineData("macro.insert")]
    public async Task CompleteNameChangeOutsideScopeOnSecondRead_IsStale(string op)
    {
        var fake = new LocalBridge();
        fake.NameChange = (data, call) =>
        {
            if (call == 3)
            {
                data.Names.Add(new() { Kind = "function", Name = "+REMOTE-UNPLACED" });
            }
        };
        PlanBaselineRead result = await fake.ReadAsync([Request(op)]);
        Assert.Equal(ErrorReason.PlanStale, result.Reason);
        Assert.Null(result.Baseline);
        Assert.Contains(result.Conflicts, c => c.Scope == "names");
        Assert.Equal(3, fake.NameReads);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("replace")]
    [InlineData("page")]
    [InlineData("pageLess")]
    [InlineData("main")]
    [InlineData("kind")]
    [InlineData("type")]
    [InlineData("name")]
    [InlineData("empty")]
    [InlineData("project")]
    [InlineData("path")]
    public async Task SameNameClosureChangeOnSecondRead_IsStale_EvenWithUnchangedNamesHash(string fault)
    {
        var fake = new LocalBridge();
        fake.FunctionChange = (data, call) =>
        {
            if (call != 3)
            {
                return;
            }
            PlanFunction function = data.Functions[^1];
            switch (fault)
            {
                case "add":
                    data.Functions.Add(new() { Type = 17, Oid = 999, Name = "+A1-K1", Page = "+A10/1" });
                    break;
                case "remove":
                    data.Functions.RemoveAt(data.Functions.Count - 1);
                    break;
                case "replace":
                    function.Oid = 999;
                    break;
                case "page":
                    function.Page = "+A10/1";
                    break;
                case "pageLess":
                    function.Page = null;
                    break;
                case "main":
                    function.IsMain = !function.IsMain;
                    break;
                case "kind":
                    function.Kind = "page";
                    break;
                case "type":
                    function.Type++;
                    break;
                case "name":
                    function.Name = "+A1-K2";
                    break;
                case "empty":
                    data.Functions.Clear();
                    break;
                case "project":
                    data.Project = "Other";
                    break;
                case "path":
                    data.ProjectPath = @"D:\Copy\Line1";
                    break;
            }
        };
        string before = fake.Names().NamesHash!;
        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => fake.ReadAsync([Request("device.update")]));
        Assert.Equal(ErrorReason.PlanStale, error.Error.Reason);
        Assert.Equal(before, fake.Names().NamesHash);
        Assert.Equal(3, fake.FunctionReads);
        Assert.Equal(2, fake.Snapshots.Count);
        Assert.All(fake.Snapshots, a => Assert.Equal(["+A1/1", "+A1/2"], a.Pages));
    }

    [Fact]
    public async Task ClosureExpandsAfterDiscovery_DoesNotExpandRequestedPages()
    {
        var fake = new LocalBridge();
        fake.FunctionChange = (data, call) =>
        {
            if (call == 2)
            {
                data.Functions.Add(new() { Type = 17, Oid = 99, Name = "+A1-K1", Page = "+A10/1" });
            }
        };
        PlanBaselineRead result = await fake.ReadAsync([Request("device.update")]);
        Assert.Equal(ErrorReason.PlanStale, result.Reason);
        Assert.Contains(result.Conflicts, c => c.Scope == "closure" && c.Reason == "expanded");
        Assert.Single(fake.Snapshots);
    }

    [Fact]
    public async Task ClosureOrderChanges_CompareAsExactSets()
    {
        var fake = new LocalBridge();
        fake.FunctionChange = (data, call) =>
        {
            if (call == 3)
            {
                data.Functions.Reverse();
            }
        };
        Assert.NotNull((await fake.ReadAsync([Request("device.update")])).Baseline);
        Assert.Equal(3, fake.FunctionReads);
    }

    [Fact]
    public async Task StandardContentChangeOnSecondRead_ChangesMembersAndPages()
    {
        var fake = new LocalBridge();
        fake.SnapshotChange = (data, call) =>
        {
            if (call == 2)
            {
                SnapshotObject function = data.Pages[0].Objects[1];
                function.Hash = "changed-property";
                data.Pages[0].Hash = PlanSnapshotHashes.Page(data.Pages[0].Objects);
            }
        };
        PlanBaselineRead result = await fake.ReadAsync([Request("device.update")]);
        Assert.Equal(ErrorReason.PlanStale, result.Reason);
        Assert.Contains(result.Conflicts, c => c.Scope == "pages");
        Assert.Contains(result.Conflicts, c => c.Scope == "members");
        Assert.DoesNotContain(result.Conflicts, c => c.Scope == "names");
    }

    [Theory]
    [InlineData("guid")]
    [InlineData("path")]
    [InlineData("project")]
    public async Task ProjectIdentityChange_WithEmptyReadScope_IsStale(string fault)
    {
        var fake = new LocalBridge();
        fake.NameChange = (data, call) =>
        {
            if (call == 3)
            {
                switch (fault)
                {
                    case "guid":
                        data.ProjectGuid = "new-guid";
                        break;
                    case "path":
                        data.ProjectPath = @"D:\Copy\Line1";
                        break;
                    case "project":
                        data.Project = "Other";
                        break;
                }
            }
        };
        PlanBaselineRead result = await fake.ReadAsync([Request("page.create")]);
        Assert.Equal(ErrorReason.PlanStale, result.Reason);
        Assert.Contains(result.Conflicts, c => c.Scope == "project");
        Assert.Empty(fake.Snapshots);
    }

    [Theory]
    [InlineData("page.renumber")]
    [InlineData("device.renumber")]
    public async Task Renumber_MoreThanFiftyProjectPages_RejectsFromInventoryBeforeAnySnapshot(string op)
    {
        var fake = new LocalBridge(PlanScopeTestData.Baseline([], [.. Enumerable.Range(1, 51).Select(i => "+A/" + i)]));
        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => fake.ReadAsync([Request(op, "+A/1")]));
        Assert.Equal(ErrorReason.CapabilityMissing, error.Error.Reason);
        Assert.Equal("plan.renumber:whole_project_guard", error.Error.Metadata!["missing"]);
        Assert.Equal("51", error.Error.Metadata["pages"]);
        Assert.Empty(fake.Snapshots);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Renumber_PageInventoryExpandsDuringDoubleRead_IsStaleWithoutGrowingScope(int at)
    {
        var fake = new LocalBridge();
        fake.NameChange = (data, call) =>
        {
            if (call == at)
            {
                data.Names.AddRange(Enumerable.Range(0, 51).Select(i => new PlanNameEntry { Kind = "page", Name = "+REMOTE/" + i }));
            }
        };
        PlanBaselineRead result = await fake.ReadAsync([Request("page.renumber")]);
        Assert.Equal(ErrorReason.PlanStale, result.Reason);
        Assert.Contains(result.Conflicts, c => c.Scope == "pages" && c.Reason == "expanded");
        Assert.Equal(at - 1, fake.Snapshots.Count);
        Assert.All(fake.Snapshots, a => Assert.Equal(3, a.Pages!.Count));
    }

    [Fact]
    public async Task Renumber_ActualObjectsOverTenThousand_RejectsBeforeCompilation()
    {
        var fake = new LocalBridge(PlanScopeTestData.Baseline(Enumerable.Range(11, 9998).Select(i => PlanScopeTestData.Function(i, "+A1/1"))));
        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => fake.ReadAsync([Request("page.renumber", "+A1/1")]));
        Assert.Equal(ErrorReason.CapabilityMissing, error.Error.Reason);
        Assert.Equal("plan.renumber:whole_project_guard", error.Error.Metadata!["missing"]);
        Assert.Equal("10001", error.Error.Metadata["objects"]);
        Assert.Single(fake.Snapshots);
    }

    [Theory]
    [InlineData("names")]
    [InlineData("functions")]
    [InlineData("pageLess")]
    public async Task IncompleteEvidenceOrUnprovedPageLessWrites_AreCapabilityMissingBeforeContentReads(string fault)
    {
        var fake = new LocalBridge();
        fake.NameChange = (data, _) => data.Complete = fault != "names";
        fake.FunctionChange = (data, _) =>
        {
            data.Complete = fault != "functions";
            if (fault == "pageLess")
            {
                data.Functions.Add(new() { Type = 17, Oid = 99, Name = "+A1-K1" });
            }
        };
        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => fake.ReadAsync([Request("device.update")]));
        Assert.Equal(ErrorReason.CapabilityMissing, error.Error.Reason);
        Assert.Empty(fake.Snapshots);
    }

    [Fact]
    public async Task PaginatedLocalSnapshot_ReadsSameExplicitPagesAndComposesOriginalHashes()
    {
        var fake = new LocalBridge { PageChunkSize = 1 };
        PlanBaseline baseline = Assert.IsType<PlanBaseline>((await fake.ReadAsync([Request("device.update")])).Baseline);
        Assert.Equal(4, fake.Snapshots.Count);
        Assert.Equal(new long?[] { null, 1, null, 1 }, fake.Snapshots.Select(a => a.AfterPage));
        Assert.All(fake.Snapshots, a => Assert.Equal(["+A1/1", "+A1/2"], a.Pages));
        Assert.Equal(PlanSnapshotHashes.Compose(baseline.Snapshot.Pages), baseline.Snapshot.ComposedHash);
    }

    [Fact]
    public async Task MultipleSteps_UnionCanExceedFiftyPages_WhileEachStepStaysLocal()
    {
        string[] pages = [.. Enumerable.Range(0, 60).Select(i => "+A/" + i)];
        var fake = new LocalBridge(PlanScopeTestData.Baseline(pages.Select((p, i) =>
            PlanScopeTestData.Function(100 + i, p, i < 30 ? "+A1-K1" : "+A1-K2", main: i is 0 or 30)), pages));
        PlanBaseline baseline = Assert.IsType<PlanBaseline>((await fake.ReadAsync([Request("device.update"), Request("device.update") with { DeviceName = "+A1-K2" }])).Baseline);
        Assert.Equal(60, baseline.Snapshot.Pages.Length);
        Assert.All(fake.Snapshots, a => Assert.Equal(60, a.Pages!.Count));
        Assert.Equal(2, fake.Snapshots.Count);
    }

    [Fact]
    public async Task PredictedNewMacroPage_IsExcludedFromExistingPageUnion()
    {
        var fake = new LocalBridge();
        PlanBaselineRead result = await fake.ReadAsync([Request("page.create"), Request("macro.insert", "+NEW/1")]);
        Assert.NotNull(result.Baseline);
        Assert.Empty(result.Baseline.Snapshot.Pages);
        Assert.Empty(fake.Snapshots);
    }

    [Fact]
    public async Task CreateRequestUsingAnExistingPage_DoesNotHideThatPageFromAnotherStepsReadScope()
    {
        var fake = new LocalBridge();
        PlanBaseline baseline = Assert.IsType<PlanBaseline>((await fake.ReadAsync([Request("page.create", "+A1/1"), Request("device.update")])).Baseline);
        Assert.Equal(["+A1/1", "+A1/2"], baseline.Snapshot.Pages.Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.All(fake.Snapshots, a => Assert.Equal(["+A1/1", "+A1/2"], a.Pages));
    }

    [Fact]
    public async Task MissingRequestedPageOnFirstRead_IsStaleWithoutCompilationOrRetry()
    {
        var fake = new LocalBridge();
        PlanBaselineRead result = await fake.ReadAsync([Request("macro.insert", "+MISSING/1")]);
        Assert.Equal(ErrorReason.PlanStale, result.Reason);
        Assert.Null(result.Baseline);
        Assert.Single(fake.Snapshots);
        Assert.Equal(1, fake.NameReads);
    }

    [Fact]
    public async Task RealPlanningBetweenReads_UsesResolvedFullNewNameInsteadOfShortArgument()
    {
        var fake = new LocalBridge();
        fake.Client.Handlers[OperationNames.DeviceUpdatePlan] = args =>
        {
            DeviceUpdateArgs request = Assert.IsType<DeviceUpdateArgs>(args);
            Assert.Equal("-K9", request.NewTag);
            return PlanLocalScopeRulesTests.Plan(fake.Source, "+A1-K9");
        };
        var input = new PlanArgumentStep(1, "device.update", JsonSerializer.SerializeToElement(new { tag = "-K1", new_tag = "-K9" }), PlanStepCatalog.Tier("device.update"), []);
        PlanBaselineRead result = await fake.ReadAsync([Request("device.update")], async (baseline, impacts) =>
        {
            Assert.Equal(2, fake.NameReads);
            WritePlan plan = await new PlanStepCompiler(fake.Client).PlanAsync(baseline.Snapshot.Project.Path, input, Token);
            PlanImpact impact = PlanLocalScopeRules.Build(baseline, Assert.Single(impacts), plan);
            Assert.Equal("+A1-K9", Assert.Single(impact.Preconditions.OfType<PlanAbsent>()).Name);
        });
        Assert.NotNull(result.Baseline);
        Assert.Single(fake.Client.Calls, c => c.Operation == OperationNames.DeviceUpdatePlan);
        Assert.Equal(3, fake.NameReads);
    }

    [Fact]
    public async Task CancellationDuringCompilationWithEmptyScope_PreventsSecondRead()
    {
        var fake = new LocalBridge();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PlanLocalBaselineReader(fake.Client).ReadAsync(fake.Source.Snapshot.Project.Path,
            [Request("page.create")], (_, _) =>
            {
                cancellation.Cancel();
                return Task.CompletedTask;
            }, cancellation.Token));
        Assert.Equal(2, fake.NameReads);
        Assert.Empty(fake.Snapshots);
    }

    [Fact]
    public async Task CancellationBeforeDiscovery_MakesNoBridgeCalls()
    {
        var fake = new LocalBridge();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PlanLocalBaselineReader(fake.Client)
            .ReadAsync(fake.Source.Snapshot.Project.Path, [Request("page.create")], new CancellationToken(canceled: true)));
        Assert.Empty(fake.Client.Calls);
    }

    private static PlanImpactRequest Request(string op, string? page = null) => new(op,
        PlanScopeRules.Pages(page is not null ? [page] : op == "device.update" ? [] : [op == "page.create" ? "+NEW/1" : "+A1/1"]), "+A1-K1");

    private sealed class LocalBridge
    {
        public LocalBridge(PlanBaseline? source = null)
        {
            Source = source ?? PlanScopeTestData.Baseline();
            Client.Projects.Add(new() { Name = Source.Snapshot.Project.Name, Path = Source.Snapshot.Project.Path });
            Client.Handlers[OperationNames.PlanNames] = _ =>
            {
                PlanNamesData data = Names();
                NameReads++;
                NameChange?.Invoke(data, NameReads);
                data.NamesHash = PlanSnapshotHashes.Names(data.ProjectPath, data.Names.Select(n => new PlanOccupiedName(n.Kind, n.Name)));
                return data;
            };
            Client.Handlers[OperationNames.PlanFunctions] = args =>
            {
                PlanFunctionsArgs request = Assert.IsType<PlanFunctionsArgs>(args);
                Assert.Null(request.Pages);
                var data = new PlanFunctionsData
                {
                    Project = Source.Snapshot.Project.Name,
                    ProjectPath = Source.Snapshot.Project.Path,
                    Complete = true,
                    Limit = 10_000,
                    Functions = [.. PlanScopeTestData.Functions(Source).Where(f => request.Names!.Contains(f.Name, StringComparer.Ordinal))],
                };
                FunctionReads++;
                FunctionChange?.Invoke(data, FunctionReads);
                return data;
            };
            Client.Handlers[OperationNames.ProjectSnapshot] = args => Snapshot(Assert.IsType<ProjectSnapshotArgs>(args));
        }

        public PlanBaseline Source { get; }
        public FakeBridgeClient Client { get; } = new()
        {
            Status = new([], new() { Operations = [OperationNames.PlanNames, OperationNames.PlanFunctions, OperationNames.ProjectSnapshot] }, null),
        };
        public List<ProjectSnapshotArgs> Snapshots { get; } = [];
        public List<PlanNameEntry> ExtraNames { get; set; } = [];
        public Action<PlanNamesData, int>? NameChange { get; set; }
        public Action<PlanFunctionsData, int>? FunctionChange { get; set; }
        public Action<ProjectSnapshotData, int>? SnapshotChange { get; set; }
        public int NameReads { get; private set; }
        public int FunctionReads { get; private set; }
        public int PageChunkSize { get; set; } = 1000;

        public PlanNamesData Names() => new()
        {
            Project = Source.Snapshot.Project.Name,
            ProjectPath = Source.Snapshot.Project.Path,
            ProjectGuid = Source.Snapshot.Project.Guid,
            Complete = true,
            Names = [.. Source.Names.Select(n => new PlanNameEntry { Kind = n.Kind, Name = n.Name }), .. ExtraNames],
            NamesHash = PlanSnapshotHashes.Names(Source.Snapshot.Project.Path, Source.Names.Concat(ExtraNames.Select(n => new PlanOccupiedName(n.Kind, n.Name)))),
        };

        public Task<PlanBaselineRead> ReadAsync(PlanImpactRequest[] requests, Func<PlanBaseline, IReadOnlyList<PlanLocalImpact>, Task>? compile = null) =>
            new PlanLocalBaselineReader(Client).ReadAsync(Source.Snapshot.Project.Path, requests, compile ?? ((_, _) => Task.CompletedTask), Token);

        private ProjectSnapshotData Snapshot(ProjectSnapshotArgs args)
        {
            Assert.NotNull(args.Pages);
            Assert.InRange(args.Pages.Count, 1, 1000);
            Snapshots.Add(args);
            List<SnapshotPage> wanted = [.. Source.Snapshot.Pages.Where(p => args.Pages.Contains(p.Name, StringComparer.Ordinal)).OrderBy(p => p.Oid)];
            List<SnapshotPage> remaining = [.. wanted.Where(p => p.Oid > (args.AfterPage ?? 0))];
            var data = new ProjectSnapshotData
            {
                Project = Source.Snapshot.Project.Name,
                ProjectPath = Source.Snapshot.Project.Path,
                ProjectGuid = Source.Snapshot.Project.Guid,
                Profile = SnapshotProfiles.StandardName,
                Format = SnapshotProfiles.Format,
                Pages = JsonSerializer.Deserialize<List<SnapshotPage>>(JsonSerializer.Serialize(remaining.Take(PageChunkSize)))!,
                PageCount = wanted.Count,
                MissingPages = [.. args.Pages.Except(wanted.Select(p => p.Name), StringComparer.Ordinal)],
                Complete = remaining.Count <= PageChunkSize,
                NextAfterPage = remaining.Count <= PageChunkSize ? null : remaining[PageChunkSize - 1].Oid,
            };
            SnapshotChange?.Invoke(data, Snapshots.Count);
            return data;
        }
    }
}
