using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ConnectionDefinitionPointTests
{
    [Fact]
    public void ExistingConnection_StandaloneDefinitionPoint_PreviewsWithoutSymbolsOrNewConnections()
    {
        var fixture = Existing();
        JObject plan = fixture.Plan();
        JToken point = Assert.Single(plan["definitionPoints"]!);
        Assert.Equal([8d, 24d], point["position"]!.Values<double>());
        Assert.Equal("CDP", (string?)point["symbol"]!["name"]);
        Assert.Equal("RD", (string?)point["properties"]!["color"]);
        Assert.Equal("1.5", (string?)point["properties"]!["cross_section"]);
        Assert.Equal(["17/2/0", "17/1/0"], point["endpoints"]!.Values<string>());
        Assert.Empty(plan["symbols"]!);
        Assert.Empty(plan["graphicalAutoconnect"]!);
        Assert.Empty(plan["logicalAfterGenerate"]!);
        Assert.Equal(1, (int)plan["undoSteps"]!);
        Assert.True((bool)plan["dryRun"]!);
        Assert.Equal(ExecutionState.NotStarted, (string?)plan["execution"]);
    }

    [Theory]
    [InlineData("{}", "")]
    [InlineData("{\"color\":\"\"}", "/color")]
    [InlineData("{\"cross_section\":\"NaN\"}", "/cross_section")]
    [InlineData("{\"color\":\"RD\",\"symbol\":\"PDP\"}", "/symbol")]
    [InlineData("{\"color\":\"RD\",\"symbol_variant\":9}", "/symbol_variant")]
    [InlineData("{\"color\":\"RD\",\"symbol_variant\":\"0\"}", "/symbol_variant")]
    [InlineData("{\"designation\":null}", "/designation")]
    [InlineData("{\"color\":\"RD\",\"unknown\":1}", "/unknown")]
    public void ExistingConnection_InvalidDefinitionPoint_IsRejectedBeforeWriting(string properties, string field)
    {
        var fixture = Existing();
        var item = new JObject { ["kind"] = "definition_point", ["position"] = new JArray(8d, 24d) };
        foreach (JProperty property in JObject.Parse(properties).Properties())
        { item[property.Name] = property.Value; }
        fixture.Request["items"] = new JArray(item);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fixture.Plan()).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Contains("/items/0" + field, error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingConnection_PartOnly_UsesDefaults()
    {
        var fixture = Existing();
        fixture.Request["items"] = JArray.Parse("[{\"kind\":\"definition_point\",\"position\":[8,24],\"part\":\"PXC.2861344\"}]");
        JToken point = Assert.Single(fixture.Plan()["definitionPoints"]!);
        Assert.Empty(point["properties"]!);
        Assert.Equal("PXC.2861344", (string?)point["articles"]![0]!["part"]);
        Assert.Equal("1", (string?)point["articles"]![0]!["variant"]);
        Assert.Equal(1u, (uint)point["articles"]![0]!["count"]!);
    }

    [Theory]
    [InlineData("{\"part\":\"missing\"}", ErrorReason.PartNotFound)]
    [InlineData("{\"part\":\"PXC.2861344\",\"part_variant\":\"2\"}", ErrorReason.InvalidArgument)]
    [InlineData("{\"part\":\"PXC.2861344\",\"part_count\":0}", ErrorReason.InvalidArgument)]
    [InlineData("{\"part\":\"PXC.2861344\",\"part_count\":1.5}", ErrorReason.InvalidArgument)]
    [InlineData("{\"part\":\"PXC.2861344\",\"part_count\":4294967296}", ErrorReason.InvalidArgument)]
    [InlineData("{\"part_count\":2}", ErrorReason.InvalidArgument)]
    public void InvalidPart_UsesExistingErrorCodes(string fields, string reason)
    {
        var fixture = Existing();
        foreach (JProperty property in JObject.Parse(fields).Properties())
        { fixture.Request["items"]![0]![property.Name] = property.Value; }
        Assert.Equal(reason, Assert.Throws<BridgeErrorException>(() => fixture.Plan()).Error.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("articles")]
    [InlineData("connectionArticles")]
    public void PartReferences_ReadBackOnPointAndConnection(string? mismatch)
    {
        var creator = new Creator(existing: true) { Mismatch = mismatch };
        creator.Fixture.Request["items"]![0]!["part"] = "PXC.2861344";
        creator.Fixture.Request["items"]![0]!["part_count"] = 2;
        if (mismatch is not null)
        { Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(() => creator.Execute()).Error.Execution); }
        else
        { Assert.Equal(2u, (uint)creator.Execute()["definitionPoints"]![0]!["connectionArticles"]![0]!["count"]!); }
    }

    [Fact]
    public void ExistingConnection_PositionIsRequired()
    {
        var fixture = Existing();
        ((JObject)fixture.Request["items"]![0]!).Remove("position");
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fixture.Plan()).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("/items/0/position", error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CDP")]
    [InlineData("CDPNG")]
    public void ExistingConnection_OffLinePosition_ReportsNearestSegment(string symbol)
    {
        var fixture = Existing();
        fixture.Request["items"]![0]!["symbol"] = symbol;
        fixture.Request["items"]![0]!["position"] = new JArray(12d, 24d);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fixture.Plan()).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("/items/0/position", error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Contains("nearest", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("40", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CDP", 0, 8d)]
    [InlineData("CDPNG", 0, 8d)]
    [InlineData("CDP", 2, 10d)]
    public void ExistingConnection_ExplicitSymbolVariantAndOffGridPosition_ArePreserved(string symbol, int variant, double x)
    {
        var fixture = Existing(x);
        fixture.Symbols.Add(ConnectionCreateFixture.Variant("CDP", "ConnectionDefinitionPoint", 2));
        fixture.Request["items"]![0]!["symbol"] = symbol;
        fixture.Request["items"]![0]!["symbol_variant"] = variant;
        fixture.Request["items"]![0]!["position"] = new JArray(x, 14.5d);
        JToken point = Assert.Single(fixture.Plan()["definitionPoints"]!);
        Assert.Equal([x, 14.5d], point["position"]!.Values<double>());
        Assert.Equal((symbol, variant), ((string?)point["symbol"]!["name"], (int)point["symbol"]!["variant"]!));
    }

    [Theory]
    [InlineData("alone")]
    [InlineData("wire")]
    [InlineData("node")]
    public void ExistingConnection_PreviewCompositions_PreserveExistingAndNewDefinitionPoints(string composition)
    {
        var fixture = Existing();
        fixture.Request.Remove("dry_run");
        var items = (JArray)fixture.Request["items"]!;
        if (composition == "wire")
        {
            JObject from = fixture.AddPin(3, [48, 8], "Up", [46, 4, 50, 8]);
            JObject to = fixture.AddPin(4, [48, 40], "Down", [46, 40, 50, 44]);
            JObject wire = ConnectionCreateFixture.Wire(from, to, [48, 8], [48, 40]);
            wire["definition_point"] = new JObject { ["color"] = "BU" };
            items.Add(wire);
        }
        if (composition == "node")
        { items.Add(ConnectionCreateFixture.Node("corner", [64, 64], "Left", "Up")); }
        JObject plan = fixture.Plan();
        Assert.True((bool)plan["dryRun"]!);
        Assert.Equal(ExecutionState.NotStarted, (string?)plan["execution"]);
        Assert.Equal(composition == "wire" ? 2 : 1, plan["definitionPoints"]!.Count());
        Assert.Equal(1, (int)plan["undoSteps"]!);
        Assert.Equal([8d, 24d], plan["definitionPoints"]![0]!["position"]!.Values<double>());
        if (composition == "wire")
        { Assert.Equal([48d, 24d], plan["definitionPoints"]![1]!["position"]!.Values<double>()); }
        Assert.Single(fixture.Scene.Graphical);
        Assert.Single(fixture.Scene.Logical);
    }

    [Theory]
    [InlineData("cable_definition")]
    [InlineData("interruption_point")]
    public void ExistingConnection_CableOrInterruptionMixedIn_IsRejected(string kind)
    {
        var fixture = Existing();
        var other = kind == "cable_definition" ? new JObject { ["kind"] = kind } : new JObject
        {
            ["kind"] = kind,
            ["name"] = "-BP1",
            ["direction"] = "Up",
            ["position"] = new JArray(64, 64),
            ["counterpart_pages"] = new JArray("=F+A1&EFS1/2")
        };
        ((JArray)fixture.Request["items"]!).Add(other);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fixture.Plan()).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("separately", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NewWire_StandalonePointCannotUseTheSameCallsNewPath()
    {
        var fixture = Straight();
        ((JArray)fixture.Request["items"]!).Add(Existing().Request["items"]![0]!.DeepClone());
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fixture.Plan()).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("/items/1/position", error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingConnection_PathThroughCornerPins_ResolvesItsLogicalEndpoints(bool offsetPins)
    {
        var fixture = Existing();
        fixture.Scene.Page.Placements.Clear();
        fixture.Scene.Pins.Clear();
        fixture.AddPin(1, [8, 8], "Up", [6, 4, 10, 8]);
        fixture.AddPin(2, [40, 40], "Left", [40, 38, 44, 42]);
        fixture.AddPin(3, [8, 40], "Down", [6, 38, 12, 42]);
        fixture.Scene.Page.Placements[^1].Kind = "SymbolReference";
        double x = offsetPins ? 10 : 8;
        fixture.Scene.Pins.Add("17/3/1", new() { Location = [x, 40], Direction = "Right" });
        fixture.Scene.Graphical = [[[8, 8], [8, 40]], [[x, 40], [40, 40]]];
        fixture.Request["items"]![0]!["position"] = new JArray(24d, 40d);
        JToken point = Assert.Single(fixture.Plan()["definitionPoints"]!);
        Assert.Equal(["17/2/0", "17/1/0"], point["endpoints"]!.Values<string>());
        Assert.Equal([24d, 40d], point["position"]!.Values<double>());
    }

    [Fact]
    public void ExistingConnection_OnlyDefinitionPoint_CommitsAndKeepsAllEndpointsInOneUndoStep()
    {
        var creator = new Creator(existing: true);
        JObject result = creator.Execute();
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal(["place", "define", "generate", "readback"], creator.Events);
        Assert.Equal(1, (int)result["undoSteps"]!);
        Assert.Equal(1, creator.Transactions.Calls.Count(c => c == "Commit"));
        Assert.Equal(1, creator.Transactions.Calls.Count(c => c == "CloseUndo"));
        JToken pair = Assert.Single(result["actualEndpoints"]!);
        Assert.Equal(["17/2/0", "17/1/0"], pair.Values<string>());
        Assert.Empty(result["symbols"]!);
        Assert.Equal("1.5", (string?)result["definitionPoints"]![0]!["connectionValues"]!["cross_section"]);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("symbol")]
    [InlineData("position")]
    [InlineData("endpoints")]
    [InlineData("properties/color")]
    [InlineData("properties/cross_section")]
    [InlineData("properties/designation")]
    [InlineData("connectionValues/color")]
    [InlineData("connectionValues/cross_section")]
    [InlineData("connectionValues/designation")]
    public void ExistingConnection_ReadbackMismatch_RollsBackTheCommittedPlacement(string field)
    {
        var creator = new Creator(existing: true) { Mismatch = field };
        creator.Fixture.Request["items"]![0]!["designation"] = "W1";
        BridgeError error = Assert.Throws<BridgeErrorException>(() => creator.Execute()).Error;
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Contains("definitionPoints/0/" + field, error.Message, StringComparison.Ordinal);
        Assert.Equal(0, creator.State);
        Assert.Contains("Commit", creator.Transactions.Calls);
        Assert.Contains("Rollback", creator.Transactions.Calls);
        Assert.DoesNotContain("CloseUndo", creator.Transactions.Calls);
    }

    [Theory]
    [InlineData("lost")]
    [InlineData("added")]
    [InlineData("duplicate")]
    public void ExistingConnection_EndpointSetChangedAfterGeneration_RollsBack(string failure)
    {
        var creator = new Creator(existing: true)
        {
            GeneratedEndpoints = failure switch
            {
                "lost" => [],
                "added" => [["17/2/0", "17/1/0"], ["17/2/0", "17/9/0"]],
                _ => [["17/2/0", "17/1/0"], ["17/2/0", "17/1/0"]]
            }
        };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => creator.Execute()).Error;
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Contains("connection", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, creator.State);
        Assert.Contains("Rollback", creator.Transactions.Calls);
        Assert.DoesNotContain("readback", creator.Events);
    }

    [Fact]
    public void ExistingConnection_NewBranchCannotHideADuplicatePreservedConnection()
    {
        var creator = new Creator
        {
            GeneratedEndpoints = [["17/1/0", "17/2/0"], ["17/1/0", "17/2/0"], ["17/2/0", "17/3/0"]],
            Drawn = [[[16, 8], [16, 24]], [[16, 24], [16, 40]], [[16, 24], [32, 24]], [[32, 24], [32, 8]]]
        };
        creator.Fixture.SplitLine(branchPin: true);
        creator.Fixture.Symbols.Add(ConnectionCreateFixture.Variant("CDP", "ConnectionDefinitionPoint", 0));
        ((JArray)creator.Fixture.Request["items"]!).Add(new JObject
        {
            ["kind"] = "definition_point",
            ["position"] = new JArray(16d, 16d),
            ["color"] = "RD"
        });
        JObject plan = creator.Fixture.Plan();
        Assert.Equal(["17/1/0", "17/2/0", "17/3/0"], Assert.Single(plan["logicalAfterGenerate"]!)["pins"]!.Values<string>());

        BridgeError error = Assert.Throws<BridgeErrorException>(() => creator.Execute()).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Contains("connection", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, creator.State);
        Assert.Contains("Rollback", creator.Transactions.Calls);
        Assert.DoesNotContain("readback", creator.Events);
    }

    [Theory]
    [InlineData("{}", "definition_point")]
    [InlineData("null", "definition_point")]
    [InlineData("{\"color\":\"\"}", "color")]
    [InlineData("{\"color\":\"  \"}", "color")]
    [InlineData("{\"color\":7}", "color")]
    [InlineData("{\"designation\":null}", "designation")]
    [InlineData("{\"cross_section\":\"abc\"}", "cross_section")]
    [InlineData("{\"cross_section\":\"1,2,3\"}", "cross_section")]
    [InlineData("{\"cross_section\":\"1.2,3\"}", "cross_section")]
    [InlineData("{\"cross_section\":\"NaN\"}", "cross_section")]
    [InlineData("{\"cross_section\":\"Infinity\"}", "cross_section")]
    [InlineData("{\"cross_section\":\"1e999\"}", "cross_section")]
    [InlineData("{\"cross_section\":1.5}", "cross_section")]
    [InlineData("{\"color\":\"RD\",\"symbol\":\"PDP\"}", "symbol")]
    [InlineData("{\"color\":\"RD\",\"symbol_variant\":9}", "symbol_variant")]
    [InlineData("{\"color\":\"RD\",\"symbol_variant\":\"0\"}", "symbol_variant")]
    [InlineData("{\"color\":\"RD\",\"position\":[16,12]}", "position")]
    [InlineData("{\"color\":\"RD\",\"position\":[16]}", "position")]
    [InlineData("{\"color\":\"RD\",\"unknown\":1}", "unknown")]
    public void InvalidDefinitionPoint_IsRejectedBeforeWriting(string definition, string field)
    {
        ConnectionCreateFixture fixture = Fixture();
        fixture.Request["items"]![0]!["definition_point"] = JToken.Parse(definition);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fixture.Plan()).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Contains("/items/0/definition_point" + (field == "definition_point" ? "" : "/" + field), error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1.5")]
    [InlineData("1e2")]
    [InlineData(" 1.5 ")]
    [InlineData("+1.5")]
    [InlineData(".5")]
    [InlineData("1,5")]
    [InlineData(",5")]
    public void CrossSection_FiniteNumericText_IsPreserved(string value)
    {
        var fixture = Fixture();
        fixture.Request["items"]![0]!["definition_point"]!["cross_section"] = value;
        Assert.Equal(value, (string?)Assert.Single(fixture.Plan()["definitionPoints"]!)["properties"]!["cross_section"]);
    }

    [Theory]
    [InlineData("1,5", "1.5")]
    [InlineData("1.5", "1,5")]
    [InlineData("1,50", "1.500")]
    public void CrossSection_DecimalSeparators_ReadBackAsTheSameNumber(string requested, string observed)
    {
        var creator = new Creator { ObservedSection = observed };
        creator.Fixture.Request["items"]![0]!["definition_point"]!["cross_section"] = requested;
        JObject result = creator.Execute();
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal(observed, (string?)result["definitionPoints"]![0]!["connectionValues"]!["cross_section"]);
        Assert.DoesNotContain("Rollback", creator.Transactions.Calls);
    }

    [Theory]
    [InlineData("1,6")]
    [InlineData("not numeric")]
    [InlineData("1,2,3")]
    public void CrossSection_DifferentOrInvalidReadback_RollsBack(string observed)
    {
        var creator = new Creator { ObservedSection = observed };
        creator.Fixture.Request["items"]![0]!["definition_point"]!["cross_section"] = "1,5";
        BridgeError error = Assert.Throws<BridgeErrorException>(() => creator.Execute()).Error;
        Assert.Equal(ExecutionState.RolledBack, error.Execution);
        Assert.Contains("cross_section", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CDP", 0)]
    [InlineData("CDPNG", 0)]
    [InlineData("CDP", 2)]
    public void Preview_UsesActualSymbolVariantAndFirstSegmentMidpoint(string symbol, int variant)
    {
        var fixture = Fixture();
        fixture.Symbols.Add(ConnectionCreateFixture.Variant("CDP", "ConnectionDefinitionPoint", 2));
        fixture.Request["items"]![0]!["definition_point"]!["symbol"] = symbol;
        fixture.Request["items"]![0]!["definition_point"]!["symbol_variant"] = variant;
        JObject plan = fixture.Plan();
        JToken point = Assert.Single(plan["definitionPoints"]!);
        Assert.Equal([16d, 8d], point["position"]!.Values<double>());
        Assert.Equal((symbol, variant), ((string?)point["symbol"]!["name"], (int)point["symbol"]!["variant"]!));
        Assert.Equal("RD", (string?)point["properties"]!["color"]);
        Assert.Equal("1.5", (string?)point["properties"]!["cross_section"]);
        Assert.Equal(1, (int)plan["undoSteps"]!);
        Assert.Empty(fixture.Scene.PointRoutes);
        Assert.Equal((string?)plan["fingerprint"], (string?)fixture.Plan()["fingerprint"]);
    }

    [Theory]
    [InlineData(24, 12)]
    [InlineData(14.5, 8)]
    public void ExplicitPosition_OnAnySegment_IsPreserved(double x, double y)
    {
        var fixture = Fixture();
        fixture.Request["items"]![0]!["definition_point"]!["position"] = new JArray(x, y);
        Assert.Equal([x, y], Assert.Single(fixture.Plan()["definitionPoints"]!)["position"]!.Values<double>());
    }

    [Fact]
    public void DefaultMidpoint_OnOffGridPinLine_RemainsOnTheWire()
    {
        var fixture = Straight(offGrid: true);
        JToken point = Assert.Single(fixture.Plan()["definitionPoints"]!);
        Assert.Equal([10d, 24d], point["position"]!.Values<double>());
    }

    [Fact]
    public void DefinitionPointSymbolEvidenceChanges_MakesPlanStale()
    {
        var fixture = Fixture();
        string fingerprint = (string)fixture.Plan()["fingerprint"]!;
        fixture.Request["items"]![0]!["definition_point"]!["symbol"] = "CDPNG";
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => fixture.Plan(fingerprint)).Error.Reason);
    }

    [Fact]
    public void DefinitionPoints_CannotBeSilentlyIgnoredByCableBranch()
    {
        var fixture = Fixture();
        ((JArray)fixture.Request["items"]!).Add(new JObject { ["kind"] = "cable_definition" });
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fixture.Plan()).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("separately", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StraightWireWithDefinitionPoint_GeneratesAfterTheCommit_InOneUndoStep()
    {
        var creator = new Creator();
        JObject result = creator.Execute();
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal(1, (int)result["undoSteps"]!);
        Assert.Equal(["place", "define", "generate", "readback"], creator.Events);
        Assert.Equal(1, creator.Transactions.Calls.Count(c => c == "Commit"));
        Assert.Equal(1, creator.Transactions.Calls.Count(c => c == "CloseUndo"));
        Assert.Equal("RD", (string?)Assert.Single(result["definitionPoints"]!)["connectionValues"]!["color"]);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("symbol")]
    [InlineData("position")]
    [InlineData("endpoints")]
    [InlineData("properties/color")]
    [InlineData("properties/cross_section")]
    [InlineData("properties/designation")]
    [InlineData("connectionValues/color")]
    [InlineData("connectionValues/cross_section")]
    [InlineData("connectionValues/designation")]
    public void ReadbackMismatch_RollsBackAndReportsTheExactField(string field)
    {
        var creator = new Creator { Mismatch = field };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => creator.Execute()).Error;
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Contains("definitionPoints/0/" + field, error.Message, StringComparison.Ordinal);
        Assert.Equal(0, creator.State);
        Assert.Contains("Rollback", creator.Transactions.Calls);
        Assert.DoesNotContain("CloseUndo", creator.Transactions.Calls);
    }

    [Fact]
    public void GenerationFailure_RollsBackTheCommittedPlacement()
    {
        var creator = new Creator { Mismatch = "generate" };
        Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(() => creator.Execute()).Error.Execution);
        Assert.Contains("Commit", creator.Transactions.Calls);
        Assert.Contains("Rollback", creator.Transactions.Calls);
        Assert.Equal(0, creator.State);
    }

    [Fact]
    public void MissingCreatedPoint_RollsBackTheCommittedWrite()
    {
        var creator = new Creator { Mismatch = "missing" };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => creator.Execute()).Error;
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Contains("definitionPoints/count", error.Message, StringComparison.Ordinal);
        Assert.Contains("Commit", creator.Transactions.Calls);
        Assert.Contains("Rollback", creator.Transactions.Calls);
        Assert.Equal(0, creator.State);
    }

    [Fact]
    public void Layout_UsesInsertionPointRatherThanOffsetOutlineCentreForOwningWire()
    {
        var point = LayoutGuard.Proposed("ConnectionDefinitionPoint", [16, 8], [14, 7, 20, 13]);
        var model = new LayoutGuard.Model { Page = new() { GridSize = 4, PageSize = [100, 100] }, Config = new() { MarginMm = 0 } };
        model.PointRoutes[$"0/{point.Locator.Oid}"] = [[[8, 8], [24, 8]]];
        model.Lines = [new() { Points = [[8, 8], [24, 8]], Changed = true }];
        Assert.DoesNotContain(LayoutGuard.Check(model, [point], suggest: false), v => v.Rule == "R4");
        model.Lines = [new() { Points = [[18, 4], [18, 20]], Changed = true }];
        Assert.Contains(LayoutGuard.Check(model, [point], suggest: false), v => v.Rule == "R4");
    }

    private static ConnectionCreateFixture Fixture()
    {
        var fixture = new ConnectionCreateFixture();
        fixture.Symbols.Add(ConnectionCreateFixture.Variant("CDP", "ConnectionDefinitionPoint", 0));
        fixture.Symbols.Add(ConnectionCreateFixture.Variant("CDPNG", "ConnectionDefinitionPoint", 0));
        fixture.Request["items"]![0]!["definition_point"] = new JObject { ["color"] = "RD", ["cross_section"] = "1.5" };
        return fixture;
    }
    private static ConnectionCreateFixture Straight(bool offGrid = false)
    {
        var fixture = Fixture();
        fixture.Scene.Pins.Clear();
        fixture.Scene.Page.Placements.Clear();
        double x = offGrid ? 10 : 8;
        JObject from = fixture.AddPin(1, [x, 8], "Up", [x - 2, 4, x + 2, 8]);
        JObject to = fixture.AddPin(2, [x, 40], "Down", [x - 2, 40, x + 2, 44]);
        fixture.Request["items"] = new JArray(ConnectionCreateFixture.Wire(from, to, [x, 8], [x, 40]));
        fixture.Request["items"]![0]!["definition_point"] = new JObject { ["color"] = "RD", ["cross_section"] = "1.5", ["designation"] = "W1" };
        return fixture;
    }
    private static ConnectionCreateFixture Existing(double x = 8)
    {
        var fixture = Straight(offGrid: x != 8);
        fixture.Scene.Graphical = [[[x, 8], [x, 40]]];
        fixture.Scene.Logical = [new() { StartDevice = "17/2/0", EndDevice = "17/1/0" }];
        fixture.Request["items"] = new JArray(new JObject
        {
            ["kind"] = "definition_point",
            ["position"] = new JArray(x, 24d),
            ["color"] = "RD",
            ["cross_section"] = "1.5"
        });
        return fixture;
    }
    private sealed class Creator : IConnectionCreator, IConnectionGenerator, IConnectionDefinitionPointWriter, IPartsDatabase
    {
        public ConnectionCreateFixture Fixture { get; }
        public FakeTransactions Transactions { get; } = new();
        public List<string> Events { get; } = [];
        public string? Mismatch { get; init; }
        public string? ObservedSection { get; init; }
        public List<string?[]>? GeneratedEndpoints { get; init; }
        public List<double[][]>? Drawn { get; init; }
        public int State { get; private set; }
        public Creator(bool existing = false)
        {
            Fixture = existing ? Existing() : Straight();
            Transactions.Snapshot = () => { int old = State; return () => State = old; };
        }
        public JObject Execute() => Assert.IsType<JObject>(new OperationContext("definition-test", true, null).Run(() => new ConnectionCreateExecuteOperation(this, this, Transactions)
            .Execute(new JObject { ["request"] = Fixture.Request, ["fingerprint"] = Fixture.Plan()["fingerprint"] })));
        public ConnectionCreatePlanner.Scene Read(string project, string page) => State == 0 ? Fixture.Scene : new()
        { Page = Fixture.Scene.Page, Layout = Fixture.Scene.Layout, Pins = Fixture.Scene.Pins, Graphical = Drawn ?? [.. Fixture.Scene.Graphical, .. Fixture.Plan()["graphicalAutoconnect"]!.ToObject<List<double[][]>>()!] };
        public PartsDatabaseContent Read(string? language) => Fixture.Read(language);
        public List<SymbolSearchItem> Read(string project, SymbolSearchArgs args) => Fixture.Read(project, args);
        public LayoutGuard.Model Measure(string project, string page) => LayoutGuard.From(Read(project, page));
        public Action Prepare(string project, string page, JArray symbols, Action<LayoutGuard.Model>? verifyLayout = null) => () => { Events.Add("place"); State = 1; };
        public Action PrepareDefinitionPoints(string project, string page, JArray planned) => () => { Assert.Equal(1, State); Events.Add("define"); State = 2; };
        public void Generate(string project, IReadOnlyList<string> pages)
        {
            Assert.Equal(2, State);
            Assert.Contains("Commit", Transactions.Calls); // A52: generation only sees committed placement.
            Events.Add("generate");
            if (Mismatch == "generate")
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "generation failed"); }
        }
        public JArray ReadDefinitionPoints(string project, string page, JArray planned)
        {
            Assert.Contains("Commit", Transactions.Calls);
            Events.Add("readback");
            var rows = (JArray)planned.DeepClone();
            foreach (JToken row in rows)
            {
                row["connectionValues"] = row["properties"]!.DeepClone();
                if (row["articles"] is JArray articles)
                { row["connectionArticles"] = articles.DeepClone(); }
                if (ObservedSection is { } section)
                { row["properties"]!["cross_section"] = section; row["connectionValues"]!["cross_section"] = section; }
            }
            if (Mismatch == "missing")
            { rows.Clear(); return rows; }
            if (Mismatch is { } field)
            {
                string[] path = field.Split('/');
                JToken owner = path.Length == 1 ? rows[0] : rows[0]![path[0]]!;
                owner[path[^1]] = field == "endpoints" ? new JArray("other", "network") : field == "position" ? new JArray(99d, 99d) : new JValue("wrong");
            }
            return rows;
        }
        public List<string?[]> Endpoints(string project, string page) => GeneratedEndpoints ?? [["17/2/0", "17/1/0"]];
        public ITransactionScopeFactory PlacementTransactions(string project, bool undoCommitted = false) { Assert.True(undoCommitted); return Transactions; }
        public void Undo(string project) => throw new NotSupportedException();
        public JObject Interruptions(string project, IReadOnlyList<string> pages) => throw new NotSupportedException();
        public IReadOnlyList<IReadOnlyList<ConnectionRecord>> ReadConnections(string project, IReadOnlyList<string> pages) => throw new NotSupportedException();
        public IReadOnlyList<IReadOnlyList<string>> ObjectIds(string project, IReadOnlyList<string> pages) => throw new NotSupportedException();
    }
}
