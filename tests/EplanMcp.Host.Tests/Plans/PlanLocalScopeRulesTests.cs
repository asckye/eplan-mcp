using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanLocalScopeRulesTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    public void PageCreate_OnlyNewPages_EmptyReadMembersAndAbsencePerPage(int count)
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        string[] pages = [.. Enumerable.Range(1, count).Select(i => "+NEW/" + i)];
        PlanLocalImpact local = Resolve(baseline, "page.create", pages);
        PlanImpact impact = PlanLocalScopeRules.Build(baseline, local, Plan(baseline));
        Assert.Empty(impact.ReadScope.Pages);
        Assert.Empty(impact.ReadScope.Locators);
        Assert.Equal(pages.Order(StringComparer.Ordinal), impact.ModifyScope.Pages);
        Assert.Empty(impact.Preconditions.OfType<PlanObjectHash>());
        Assert.Empty(impact.Preconditions.OfType<PlanPageHash>());
        Assert.Equal(count, impact.Preconditions.OfType<PlanAbsent>().Count());
        PlanMembers members = Assert.Single(impact.Preconditions.OfType<PlanMembers>());
        Assert.Equal(0, members.Count);
        Assert.Equal("da024fe81b16951b25f6dd30b21ec6d5f058f1eebedbb7f9638c859da255dc91", members.Hash);
    }

    [Theory]
    [InlineData("page.create", 0)]
    [InlineData("page.create", 51)]
    [InlineData("macro.insert", 0)]
    [InlineData("macro.insert", 2)]
    [InlineData("page.renumber", 0)]
    [InlineData("device.renumber", 51)]
    public void Targets_OutsideStepLimits_RejectsInsteadOfSelectingWholeProject(string op, int count)
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        BridgeCallException error = Assert.Throws<BridgeCallException>(() => Resolve(baseline, op,
            [.. Enumerable.Range(0, count).Select(i => "+A/" + i)]));
        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        Assert.StartsWith("/args/", error.Error.Metadata!["pointer"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(61)]
    [InlineData(200)]
    public void DeviceUpdate_AllSameNameFunctions_HaveExactLocatorScopesAndObjectHashes(int count)
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline(Enumerable.Range(11, count)
            .Select(i => PlanScopeTestData.Function(i, i % 2 == 0 ? "+A1/2" : "+A1/1", main: i is 11 or 12)));
        PlanLocalImpact local = Resolve(baseline, "device.update", [], PlanScopeTestData.Functions(baseline));
        PlanImpact impact = PlanLocalScopeRules.Build(baseline, local, Plan(baseline, "+A1-K9"));
        Assert.Equal(["+A1/1", "+A1/2"], impact.ReadScope.Pages);
        Assert.Empty(impact.ModifyScope.Pages);
        Assert.Empty(impact.ModifyScope.Structures);
        Assert.Equal(count, impact.ModifyScope.Locators.Length);
        Assert.Equal(count, impact.Preconditions.OfType<PlanObjectHash>().Count());
        Assert.Equal(2, impact.Preconditions.OfType<PlanPageHash>().Count());
        Assert.Equal(count + 2, Assert.Single(impact.Preconditions.OfType<PlanMembers>()).Count);
        Assert.Equal("+A1-K9", Assert.Single(impact.Preconditions.OfType<PlanAbsent>()).Name);
        Assert.All(impact.ModifyScope.Locators, l => Assert.True(PlanScopeRules.Contains(impact.ModifyScope, l)));
        Assert.Equal(PlanSnapshotHashes.Members(baseline.Snapshot.Project.Path,
            PlanScopeRules.Select(baseline.Snapshot, impact.ReadScope).Select(o => new KeyValuePair<string, string>(PlanSnapshotHashes.Key(o.Kind, o.Type, o.Oid), o.Hash))),
            Assert.Single(impact.Preconditions.OfType<PlanMembers>()).Hash);
    }

    [Theory]
    [InlineData(0, ErrorReason.CapabilityMissing)]
    [InlineData(201, ErrorReason.InvalidArgument)]
    public void DeviceUpdate_ClosureLimits_DoNotDropMembers(int count, string reason)
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        PlanFunction[] functions = [.. Enumerable.Range(1, count).Select(i => new PlanFunction
        {
            Type = 17,
            Oid = i,
            Name = "+A1-K1",
            IsMain = true,
            Page = "+A1/1",
        })];
        Assert.Equal(reason, Assert.Throws<BridgeCallException>(() => Resolve(baseline, "device.update", [], functions)).Error.Reason);
    }

    [Fact]
    public void DeviceUpdate_PageLessMember_IsInExactScopeAndRefusedUntilLP5()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        PlanFunction[] closure = [.. PlanScopeTestData.Functions(baseline).Where(f => f.Name == "+A1-K1"), new()
        {
            Type = 17,
            Oid = 99,
            Name = "+A1-K1",
            Page = null,
        }];
        PlanLocalImpact local = Resolve(baseline, "device.update", [], closure);
        PlanLocator pageLess = Assert.Single(local.ModifyScope.Locators, l => l.Oid == 99);
        Assert.True(PlanScopeRules.Contains(local.ModifyScope, pageLess));
        Assert.Equal(["+A1/1", "+A1/2"], local.ReadScope.Pages);
        BridgeCallException error = Assert.Throws<BridgeCallException>(() => PlanLocalScopeRules.Build(baseline, local, Plan(baseline, "+A1-K9")));
        Assert.Equal(ErrorReason.CapabilityMissing, error.Error.Reason);
        Assert.Equal("object.resolve:functions_without_page", error.Error.Metadata!["missing"]);
    }

    [Theory]
    [InlineData(50, true)]
    [InlineData(51, false)]
    public void DeviceUpdate_PageLimit_IsCountedIndependentlyOfEmptyModifyPages(int count, bool allowed)
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        PlanFunction[] closure = [.. Enumerable.Range(1, count).Select(i => new PlanFunction
        {
            Type = 17,
            Oid = i,
            Name = "+A1-K1",
            IsMain = i == 1,
            Page = "+A/" + i,
        })];
        if (allowed)
        {
            Assert.Equal(count, Resolve(baseline, "device.update", [], closure).ReadScope.Pages.Length);
        }
        else
        {
            Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeCallException>(() => Resolve(baseline, "device.update", [], closure)).Error.Reason);
        }
    }

    [Theory]
    [InlineData("+a1-K9")]
    [InlineData("+A1-K9:1")]
    public void DeviceUpdate_RealPlanFullName_CollidesIgnoringCaseAndColonSuffix(string occupied)
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        baseline = baseline with { Names = [.. baseline.Names, new("function", occupied)] };
        PlanLocalImpact local = Resolve(baseline, "device.update", [], PlanScopeTestData.Functions(baseline).Where(f => f.Name == "+A1-K1"));
        Assert.Throws<ArgumentException>(() => PlanLocalScopeRules.Build(baseline, local, Plan(baseline, "+A1-K9")));
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("type")]
    [InlineData("oid")]
    [InlineData("name")]
    [InlineData("main")]
    [InlineData("duplicate")]
    public void DeviceUpdate_InvalidClosure_IsRejected(string fault)
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        PlanFunction[] closure = PlanScopeTestData.Functions(baseline).Where(f => f.Name == "+A1-K1").ToArray();
        switch (fault)
        {
            case "kind":
                closure[0].Kind = "page";
                break;
            case "type":
                closure[0].Type = 0;
                break;
            case "oid":
                closure[0].Oid = 0;
                break;
            case "name":
                closure[0].Name = "wrong";
                break;
            case "main":
                closure[0].IsMain = false;
                break;
            case "duplicate":
                closure[1].Oid = closure[0].Oid;
                break;
        }
        Assert.Equal(ErrorReason.CapabilityMissing, Assert.Throws<BridgeCallException>(() => Resolve(baseline, "device.update", [], closure)).Error.Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    public void MacroInsert_CandidatesAreFiniteDistinctLiteralNames_WithTargetPageAndResourceChecks(int count)
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline([]);
        PlanMacroInspectData macro = Macro(count);
        macro.Functions.AddRange(macro.Functions.ToArray());
        macro.ObjectCount = macro.Functions.Count;
        PlanImpact impact = PlanLocalScopeRules.Build(baseline, Resolve(baseline, "macro.insert", ["+A1/1"]), Plan(baseline), macro);
        Assert.Equal(["+A1/1"], impact.ReadScope.Pages);
        Assert.Equal(impact.ReadScope, impact.ModifyScope);
        Assert.Single(impact.Preconditions.OfType<PlanPageHash>());
        Assert.Equal(count, impact.Preconditions.OfType<PlanAbsent>().Count());
        Assert.Equal(macro.Sha256, Assert.Single(impact.Preconditions.OfType<PlanResourceHash>()).Sha256);
        Assert.All(impact.Preconditions.OfType<PlanAbsent>(), p => Assert.StartsWith("+A1-K", p.Name, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("limit")]
    [InlineData("plc")]
    [InlineData("unknownStructure")]
    [InlineData("inconsistentStructure")]
    [InlineData("repeatedStructure")]
    [InlineData("incomplete")]
    [InlineData("missing")]
    public void MacroInsert_UnprovableCandidates_AreCapabilityMissing(string fault)
    {
        PlanMacroInspectData? macro = Macro(fault == "limit" ? 201 : 1);
        switch (fault)
        {
            case "plc":
                macro.Functions[0].Category = "PLCTerminal";
                break;
            case "unknownStructure":
                macro.Functions[0].StructureSegments = ["#Unknown"];
                break;
            case "inconsistentStructure":
                macro.Functions[0].StructureSegments = [];
                break;
            case "repeatedStructure":
                macro.Functions[0].StructureSegments = ["+", "+"];
                break;
            case "incomplete":
                macro.ObjectCount = 0;
                break;
            case "missing":
                macro = null;
                break;
        }
        Assert.Equal(ErrorReason.CapabilityMissing, Assert.Throws<BridgeCallException>(() => PlanLocalScopeRules.Candidates("+A1/1", macro)).Error.Reason);
    }

    [Fact]
    public void MacroInsert_QuestionMarkIsLiteral_AndUnnamedFunctionsDoNotClaimNames()
    {
        PlanMacroInspectData macro = Macro(1);
        macro.Functions[0].Name = "+-X?:?1";
        macro.Functions.Add(new() { Kind = "function", Category = "Wire", Name = "" });
        macro.ObjectCount++;
        Assert.Equal(["+A1-X?:?1"], PlanLocalScopeRules.Candidates("+A1/1", macro));
        PlanBaseline baseline = PlanScopeTestData.Baseline([]);
        baseline = baseline with { Names = [.. baseline.Names, new("function", "+A1-X?:?1:2")] };
        Assert.Throws<ArgumentException>(() => PlanLocalScopeRules.Build(baseline, Resolve(baseline, "macro.insert", ["+A1/1"]), Plan(baseline), macro));
    }

    [Fact]
    public void MacroInsert_NonemptyTarget_IsCapabilityMissing()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        Assert.Equal(ErrorReason.CapabilityMissing, Assert.Throws<BridgeCallException>(() => PlanLocalScopeRules.Build(baseline,
            Resolve(baseline, "macro.insert", ["+A1/1"]), Plan(baseline), Macro(1))).Error.Reason);
    }

    [Theory]
    [InlineData("page.renumber")]
    [InlineData("device.renumber")]
    public void Renumber_ProjectAndObjectLimits_KeepWholeScopesAndSingleCallHint(string op)
    {
        string[] pages = [.. Enumerable.Range(1, 50).Select(i => "+A/" + i)];
        PlanBaseline baseline = PlanScopeTestData.Baseline([], pages);
        PlanLocalImpact local = Resolve(baseline, op, [pages[0]]);
        Assert.Equal(pages.Order(StringComparer.Ordinal), local.ReadScope.Pages);
        Assert.Equal(local.ReadScope, local.ModifyScope);
        BridgeCallException error = Assert.Throws<BridgeCallException>(() => PlanLocalScopeRules.Resolve(baseline.Snapshot.Project,
            new(op, PlanScopeRules.Pages([pages[0]])), pages.Append("+A/51")));
        AssertRenumberError(error);
        PlanLocalScopeRules.RenumberLimit(50, 10_000);
        AssertRenumberError(Assert.Throws<BridgeCallException>(() => PlanLocalScopeRules.RenumberLimit(50, 10_001)));
    }

    [Fact]
    public void LocalHashesAndRecords_EqualOriginalGoldenVectors_WithoutNewFieldsOrTags()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline([PlanScopeTestData.Function(11, "+A1/1", main: true)]);
        PlanLocalImpact local = Resolve(baseline, "device.update", [], PlanScopeTestData.Functions(baseline));
        PlanImpact impact = PlanLocalScopeRules.Build(baseline, local, Plan(baseline, "+A1-K9"));
        Assert.Equal("3e2bd24e3232aebc3a39aef0d4870ad146c44c06afa070f796c04fe250ea8f90", Assert.Single(impact.Preconditions.OfType<PlanMembers>()).Hash);
        Assert.Equal("2412664ec1402e107b264feb2ca8f842abbd3f2f632d7dce25934ff503887dc7", baseline.NamesHash);
        Assert.Equal("0a2eca8b72ce6c1f0dabb2a706b55481c9a6725faaa9f859b55943d947f9e7f5", PlanSnapshotHashes.Compose([]));
        string json = JsonSerializer.Serialize(baseline, PlanJson.Options);
        Assert.Equal(json, JsonSerializer.Serialize(PlanJson.Read<PlanBaseline>(json), PlanJson.Options));
        Assert.Equal(["names", "namesHash", "snapshot"], JsonSerializer.SerializeToElement(baseline, PlanJson.Options).EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["absent", "members", "objectHash", "pageHash"], impact.Preconditions.Select(p =>
            JsonSerializer.SerializeToElement<PlanPrecondition>(p, PlanJson.Options).GetProperty("tag").GetString()).Distinct().Order(StringComparer.Ordinal));
    }

    private static PlanLocalImpact Resolve(PlanBaseline baseline, string op, string[] pages, IEnumerable<PlanFunction>? closure = null) =>
        PlanLocalScopeRules.Resolve(baseline.Snapshot.Project, new(op, PlanScopeRules.Pages(pages), "+A1-K1"), baseline.Snapshot.Pages.Select(p => p.Name), closure);

    internal static WritePlan Plan(PlanBaseline baseline, string? newName = null) => new()
    {
        Project = baseline.Snapshot.Project.Name,
        ProjectPath = baseline.Snapshot.Project.Path,
        Changes = newName is null ? [] : [new() { Action = "update", Kind = "device", Name = "+A1-K1", NewValue = newName }],
    };

    internal static PlanMacroInspectData Macro(int count) => new()
    {
        Sha256 = new string('a', 64),
        ObjectCount = count,
        Functions = [.. Enumerable.Range(0, count).Select(i => new PlanMacroFunction
        {
            Kind = "function",
            Category = "Coil",
            Name = "+-K" + i,
            StructureSegments = ["+"],
        })],
    };

    private static void AssertRenumberError(BridgeCallException error)
    {
        Assert.Equal(ErrorReason.CapabilityMissing, error.Error.Reason);
        Assert.Equal("plan.renumber:whole_project_guard", error.Error.Metadata!["missing"]);
        Assert.Contains("eplan_page_renumber", error.Error.Message, StringComparison.Ordinal);
        Assert.Contains("eplan_device_renumber", error.Error.Message, StringComparison.Ordinal);
        Assert.Contains("50", error.Error.Metadata["limits"], StringComparison.Ordinal);
    }
}
