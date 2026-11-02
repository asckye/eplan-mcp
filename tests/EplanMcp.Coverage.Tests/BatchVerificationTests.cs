extern alias host;
using System.Runtime.Versioning;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Large.Tests;
using host::EplanMcp.Host.Tools;

namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Small")]
[SupportedOSPlatform("windows")]
public sealed class BatchVerificationTests
{
    [Theory]
    [InlineData("O:Eplan.EplApi.DataModel.Function.UpdateLocalTemplate")]
    [InlineData("O:Eplan.EplApi.DataModel.MacroBox.RepresentationType")]
    [InlineData("O:Eplan.EplApi.DataModel.Page.ConvertToGraphics")]
    public void RefusedWritesAreSkippedBeforeLookingUpObjectsOrArguments(string group)
    {
        BatchEntry entry = Assert.Single(BatchValues.ApiEntries(), e => e.Group == group && e.Kind == "api-write");
        var result = new BatchObservation(entry.Id, entry.Kind);
        Assert.True(BatchValues.SkipRefused(entry, result));
        Assert.Equal("skipped:refused_unsafe", result.Outcome);
        Assert.False(BatchValues.Stops(result));
        if (group.EndsWith("RepresentationType", StringComparison.Ordinal))
        { Assert.False(BatchValues.SkipRefused(Assert.Single(BatchValues.ApiEntries(), e => e.Group == group && e.Kind == "api-read"), new(group, "api-read"))); }
    }

    [Theory]
    [InlineData(ErrorReason.ProjectNotOpen, true)]
    [InlineData(ErrorReason.SessionNotSelected, true)]
    [InlineData(ErrorReason.EplanNotConnected, true)]
    [InlineData(ErrorReason.EplanOperationFailed, false)]
    public void LostEplanClassifiesTheLiveConvertToGraphicsResultAsUnknown(string reason, bool alive)
    {
        var result = new BatchObservation("O:Eplan.EplApi.DataModel.Page.ConvertToGraphics#write", "api-write")
        { Outcome = "failed", Detail = reason + ": EPLAN unavailable", Seconds = 25.9 };
        Assert.True(BatchValues.EplanLost(result, alive));
        Assert.Equal("outcome_unknown", result.Outcome);
        Assert.Equal("EPLAN process exited during the trial", result.Detail);
        Assert.Equal(25.9, result.Seconds);
        Assert.True(BatchValues.Stops(result));
        Assert.True(BatchValues.EplanLost(result, processAlive: true));
    }

    [Fact]
    public void OrdinaryFailuresWithALiveEplanRemainFailures()
    {
        var result = new BatchObservation("id", "api-write") { Detail = ErrorReason.EplanOperationFailed + ": ordinary failure" };
        Assert.False(BatchValues.EplanLost(result, processAlive: true));
        Assert.Equal("failed", result.Outcome);
        Assert.False(BatchValues.Stops(result));
    }

    [Fact]
    public void ExpectedCountsMatchTheGeneratedPropertyAndMethodTables()
    {
        ActionCatalogItem row = ActionCatalogReader.Create("TestAction", "official", null, null, null, [], null, "test");
        ActionCatalogItem[] actions = [row, row];
        BatchEntry[] entries = BatchValues.Entries(actions);
        Dictionary<string, int> expected = BatchValues.ExpectedCounts(entries);
        string[] properties = [.. ApiTryCatalog.PropertyGroups.Where(g => !ApiTryCatalog.MethodGroups.Contains(g, StringComparer.Ordinal))];
        string[] methods = ApiTryCatalog.MethodGroups;
        Assert.Equal(properties.Length + methods.Count(m => ApiTryCatalog.MethodAccess(m) == "read"), expected["api-read"]);
        Assert.Equal(properties.Count(p => ApiTryCatalog.ValueShape(p) is not null) + methods.Count(m => ApiTryCatalog.MethodAccess(m) == "write"), expected["api-write"]);
        Assert.Equal(1, expected["action"]);
        Assert.Equal(properties.Order(StringComparer.Ordinal), entries.Where(e => !e.Method && e.Kind == "api-read").Select(e => e.Group).Order(StringComparer.Ordinal));
        Assert.Equal(methods.Order(StringComparer.Ordinal), entries.Where(e => e.Method).Select(e => e.Group).Order(StringComparer.Ordinal));
        Assert.Equal(["A:TestAction"], entries.Where(e => e.Kind == "action").Select(e => e.Id));
        Assert.Equal(entries.Length, entries.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count());
        foreach (BatchEntry entry in entries.Where(e => e.Method))
        { Assert.NotNull(ApiTryCatalog.Shape(entry.Group, entry.Parameters!.Keys)); }
        foreach ((string kind, int count) in expected)
        { TestContext.Current.TestOutputHelper!.WriteLine($"Expected {kind}: {count}"); }
    }

    [Theory]
    [InlineData("file")]
    [InlineData("page")]
    [InlineData("selection")]
    public async Task ActionEntriesUseOnlyOfficialTypeRowsAndPreserveContextAsync(string context)
    {
        ActionCatalogItem row = ActionCatalogReader.Create("TestAction", "official", "FIRST", null, null,
            [JsonSerializer.SerializeToElement(new { name = "MODE", required = true })], null, "test") with
        { Examples = [JsonSerializer.SerializeToElement(new { TYPE = "FIRST", MODE = "exact" })] };
        ActionCatalogItem[] rows = [row, row with { Type = "SECOND", Context = [context] }, row with { Name = "LocalOnly", Source = "ui" }];
        BatchEntry[] actions = [.. BatchValues.Entries(rows).Where(e => e.Kind == "action")];
        Assert.Equal(["A:TestAction/FIRST", "A:TestAction/SECOND"], actions.Select(e => e.Id));
        Assert.Equal("exact", (await BatchValues.ArgumentsAsync(actions[0], _ => throw new InvalidOperationException()))!["MODE"]);
        Assert.Null(await BatchValues.ArgumentsAsync(actions[1], _ => throw new InvalidOperationException()));
        Assert.Equal("FIRST", actions[0].Action!.Type);
    }

    [Fact]
    public async Task FileExamplesAndUnknownRequiredArgumentsNeedContextAsync()
    {
        ActionCatalogItem row = ActionCatalogReader.Create("TestAction", "official", null, null, null,
            [JsonSerializer.SerializeToElement(new { name = "FILENAME", type = "file", required = true })], null, "test") with
        { Examples = [JsonSerializer.SerializeToElement(new { FILENAME = "C:\\official.xml" })] };
        Assert.Equal("<scratch imports/FILENAME>", ActionCallTemplates.Example(row)["FILENAME"]!.GetValue<string>());
        foreach (ActionCatalogItem item in new[] { row, row with { Examples = null } })
        {
            BatchEntry entry = BatchValues.Entries([item]).Single(e => e.Kind == "action");
            Assert.Null(await BatchValues.ArgumentsAsync(entry, _ => throw new InvalidOperationException()));
        }
    }

    [Theory]
    [InlineData("O:Eplan.EplApi.DataModel.BoxedDevice.IsVisible", "BoxedDevice")]
    [InlineData("O:Eplan.EplApi.DataModel.Graphics.PlaceHolder.Name", "PlaceHolder")]
    [InlineData("O:Eplan.EplApi.DataModel.EObjects.Terminal.IsMainTerminal", "Terminal")]
    [InlineData("O:Eplan.EplApi.DataModel.E3D.BusBar.BarCount", "BusBar")]
    public void CatalogTypesUsePublishedDiscoveryNames(string group, string expected)
    {
        Assert.Contains(expected, BatchValues.ObjectKinds(group)!);
        foreach (string kind in BatchValues.ObjectKinds(group)!)
        { Assert.True(ObjectReadTypes.Supported(kind)); }
    }

    [Fact]
    public void EveryTargetKindUsesTheDiscoveryTable()
    {
        foreach (BatchEntry entry in BatchValues.ApiEntries())
        {
            // Indexed chain properties have no plain shape; their handle receiver needs a source call first.
            if (entry.Signature is null && ApiTryCatalog.Shape(entry.Group) is null)
            { Assert.Empty(entry.ObjectKinds!); Assert.True(entry.NeedsContext); continue; }
            string signature = entry.Signature ?? ApiTryCatalog.Shape(entry.Group)![1];
            string[]? receiver = ApiTryCatalog.ReceiverShape(signature);
            if (receiver is null)
            { Assert.Null(entry.ObjectKinds); continue; }
            string[] expected = receiver[0] is "handle" or "via" ? [] : [.. ObjectReadTypes.Types.Where(ObjectReadTypes.Supported)
                .Where(kind => ApiTryCatalog.Kinds(receiver[0]).Contains(kind, StringComparer.OrdinalIgnoreCase))];
            Assert.Equal(expected, entry.ObjectKinds);
            Assert.All(entry.ObjectKinds!, kind => Assert.True(ObjectReadTypes.Supported(kind)));
        }
        Assert.Empty(BatchValues.ObjectKinds("O:Eplan.EplApi.DataModel.PropertyPlacement.Unsupported")!);
    }

    [Fact]
    public async Task ServiceLocatorArgumentsKeepMainsProjectTargetAsync()
    {
        BatchEntry entry = BatchValues.ApiEntries().Single(e => e.Group == "O:Eplan.EplApi.HEServices.Edit.BringToFront");
        Assert.Equal(["Project"], entry.ObjectKinds!);
        JsonElement locator = JsonSerializer.SerializeToElement(new { project = "scratch", page = "=A/1", type = 1, oid = 2, digest = new string('a', 64) });
        int calls = 0;
        Dictionary<string, object?>? parameters = await BatchValues.ArgumentsAsync(entry, kinds =>
        {
            calls++;
            Assert.NotEmpty(kinds);
            Assert.All(kinds, kind => Assert.True(ObjectReadTypes.Supported(kind)));
            Assert.Contains("Placement", kinds);
            return Task.FromResult<JsonElement?>(locator);
        });
        Assert.Equal(1, calls);
        Assert.Equal("oPlacementToMove", Assert.Single(parameters!).Key);
        Assert.Equal(locator, parameters!["oPlacementToMove"]);
        parameters["target"] = locator;
        if (OperatingSystem.IsWindows())
        { Assert.True(ApiTryTools.MethodParameters(entry.Group, JsonSerializer.SerializeToElement(parameters))); }
        Assert.Null(await BatchValues.ArgumentsAsync(entry, _ => Task.FromResult<JsonElement?>(null)));
    }

    [Fact]
    public async Task NoArgumentServicesRunAndUnknownScalarArgumentsRemainSkippedAsync()
    {
        BatchEntry noArguments = BatchValues.ApiEntries().Single(e => e.Group == "O:Eplan.EplApi.HEServices.DrawingService.Reset");
        BatchEntry needsArguments = BatchValues.ApiEntries().Single(e => e.Group == "O:Eplan.EplApi.HEServices.SelectionSet.GetSelectedObject");
        Task<JsonElement?> UnexpectedLookup(string[] _) => throw new InvalidOperationException("No locator argument is declared.");
        Assert.Equal(["Project"], noArguments.ObjectKinds!);
        Assert.Empty((await BatchValues.ArgumentsAsync(noArguments, UnexpectedLookup))!);
        Assert.Null(await BatchValues.ArgumentsAsync(needsArguments, UnexpectedLookup));
    }

    [Fact]
    public void EveryReadWriteTsvGroupIsRepresentedExactlyOncePerAccess()
    {
        using var reader = new StreamReader(typeof(BatchVerificationTests).Assembly.GetManifestResourceStream("api-try.g.tsv")!);
        var rows = reader.ReadToEnd().Split('\n').Skip(1).Select(l => l.Split('\t')).Where(r => r.Length == 4 && r[2] is "read" or "write").ToArray();
        BatchEntry[] entries = BatchValues.ApiEntries();
        foreach (var group in rows.GroupBy(r => r[0], StringComparer.Ordinal))
        {
            Assert.Contains(entries, e => e.Group == group.Key);
            foreach (string access in group.Select(r => r[2]).Distinct(StringComparer.Ordinal))
            { Assert.Single(entries, e => e.Group == group.Key && e.Kind == "api-" + access); }
        }
    }

    [Fact]
    public async Task FactoryHandleFileAndPageContextUseCurrentShapesWithoutGuessingAsync()
    {
        BatchEntry[] entries = BatchValues.ApiEntries();
        BatchEntry factory = entries.Single(e => e.Group == "O:Eplan.EplApi.DataModel.Graphics.Line.Create");
        Assert.True(ApiTryCatalog.Factory(factory.Signature!));
        Assert.Null(factory.ObjectKinds);
        Assert.DoesNotContain("target", factory.Parameters!.Keys);
        BatchEntry removal = entries.Single(e => e.Group == "O:Eplan.EplApi.DataModel.Placement.Remove");
        Assert.Equal("api-write", removal.Kind);
        Assert.True(removal.Destructive);
        BatchEntry handle = entries.Single(e => e.Group == "O:Eplan.EplApi.DataModel.Pin.Name" && e.Kind == "api-read");
        Assert.True(handle.NeedsContext);
        Assert.Contains(entries, e => ApiTryCatalog.Settings(e.Group));
        BatchEntry file = entries.First(e => e.Parameters?.Any(p => ApiTryCatalog.PathDirection(e.Signature!, p.Key) is not null) == true);
        BatchEntry fileProperty = entries.Single(e => e.Group == "O:Eplan.EplApi.DataModel.Graphics.Image.FileName" && e.Kind == "api-write");
        foreach (BatchEntry entry in new[] { factory, handle, file, fileProperty })
        { Assert.Null(await BatchValues.ArgumentsAsync(entry, _ => throw new InvalidOperationException("Context must not be guessed."))); }
        BatchEntry pageBound = entries.Single(e => e.Group == "O:Eplan.EplApi.HEServices.NameService.CanDeviceTagBeNested");
        Assert.Null(await BatchValues.ArgumentsAsync(pageBound, _ => throw new InvalidOperationException("Native page context must not be guessed.")));
    }

    [Fact]
    public async Task ObjectArraysUseNonemptyFreshLocatorsAsync()
    {
        JsonElement locator = JsonSerializer.SerializeToElement(new { project = "scratch", page = "=A/1", type = 1, oid = 2, digest = new string('a', 64) });
        var entry = new BatchEntry("array", "api-read", Parameters: new(StringComparer.Ordinal) { ["objects"] = ["array", "Eplan.EplApi.DataModel.Placement"] });
        Dictionary<string, object?>? parameters = await BatchValues.ArgumentsAsync(entry, _ => Task.FromResult<JsonElement?>(locator));
        Assert.Equal(locator, Assert.Single(Assert.IsType<object[]>(parameters!["objects"])));
    }

    [Fact]
    public void MethodArgumentsUseOnlyObservedScalarsOrPublishedEnumMembers()
    {
        Assert.Equal("First", BatchValues.Argument(["enum", "First", "Second"]));
        Assert.Equal(true, BatchValues.Argument(["System.Boolean"], "True"));
        Assert.Equal(42, BatchValues.Argument(["System.Int32"], "42"));
        foreach (string type in new[] { "System.Boolean", "System.Int32", "System.String", "Eplan.EplApi.Base.PointD", "Eplan.EplApi.DataModel.Page" })
        { Assert.Null(BatchValues.Argument([type])); }
        Assert.Null(BatchValues.Argument(["System.String"], "not a method argument"));
        Assert.Empty(BatchValues.ApiEntries().Single(e => e.Group == "O:Eplan.EplApi.DataModel.Connection.MakePersistent").Parameters!);
    }

    [Theory]
    [InlineData("System.Boolean", "True", false)]
    [InlineData("System.Int32", "41", 42)]
    [InlineData("System.Int64", "41", 42L)]
    [InlineData("System.Double", "2.5", 3.0)]
    [InlineData("System.String", "page", "page_v")]
    public void WriteValueIsDerivedFromTheReadValue(string type, string current, object expected) => Assert.Equal(expected, BatchValues.Next(type, current));

    [Fact]
    public void EnumValuesAdvanceAndWrapWithoutReflection()
    {
        Assert.Equal("B", BatchValues.Next("Test.Enum", "A", ["A", "B"]));
        Assert.Equal("A", BatchValues.Next("Test.Enum", "B", ["A", "B"]));
        Assert.Null(BatchValues.Next("Test.Enum", "missing", ["A", "B"]));
        Assert.Null(BatchValues.Next("System.String", null));
        Assert.Throws<OverflowException>(() => BatchValues.Next("System.Int32", "2147483647"));
    }

    [Theory]
    [InlineData("outcome_unknown")]
    [InlineData("modal")]
    [InlineData("timeout")]
    public void UncertainOutcomesStopTheEntireRun(string outcome) => Assert.True(BatchValues.Stops(new("id", "action") { Outcome = outcome }));

    [Fact]
    public void FailedUndoAndUnexpectedReadMutationStopButOrdinaryFailuresCanContinue()
    {
        Assert.True(BatchValues.Stops(new("id", "api-write") { Outcome = "failed", UndoRestores = false }));
        Assert.True(BatchValues.Stops(new("id", "api-read") { Outcome = "failed", Changed = true }));
        Assert.False(BatchValues.Stops(new("id", "action") { Outcome = "rolled_back" }));
        Assert.False(BatchValues.Stops(new("id", "api-read") { Outcome = "skipped:no_object" }));
    }

    [Fact]
    public void ResumeIsInclusiveAndRejectsAnUnknownEntry()
    {
        BatchEntry[] entries = [new("read", "api-read"), new("write", "api-write"), new("action", "action")];
        Assert.Equal(entries, BatchValues.Resume(entries, null));
        Assert.Equal(entries[1..], BatchValues.Resume(entries, "write"));
        Assert.Throws<ArgumentException>(() => BatchValues.Resume(entries, "unknown"));
        Assert.Equal(entries[2..], BatchValues.Resume(entries, null, "write"));
        Assert.Empty(BatchValues.Resume(entries, null, "action"));
        Assert.Throws<ArgumentException>(() => BatchValues.Resume(entries, null, "unknown"));
        Assert.Throws<ArgumentException>(() => BatchValues.Resume(entries, "read", "write"));
    }

    [Theory]
    [InlineData("rolled_back", "EPLAN could not complete the action.", 0, "failed:needs_context")]
    [InlineData("rolled_back", "EPLAN could not complete the action.", 1, "rolled_back")]
    [InlineData("failed", "EPLAN could not complete the action.", 0, "failed")]
    [InlineData("rolled_back", "Another error", 0, "rolled_back")]
    public void RollbackWithoutMessagesCanRequireContext(string outcome, string detail, int messages, string expected)
    {
        var observation = new BatchObservation("A:test", "action") { Outcome = outcome, Detail = detail, EplanMessages = messages };
        BatchValues.Classify(observation);
        Assert.Equal(expected, observation.Outcome);
    }
}

[Trait("Size", "Medium")]
[SupportedOSPlatform("windows")]
public sealed class BatchActionCatalogTests
{
    [Fact]
    public async Task OfficialRowsIgnoreInstalledUiDefinitionsWithoutConnectingToEplanAsync()
    {
        if (!OperatingSystem.IsWindows())
        { return; }
        ActionCatalogData catalog = await new ActionCatalogReader().ReadAsync(TestContext.Current.CancellationToken);
        BatchEntry[] entries = BatchValues.Entries(catalog.Items);
        Assert.Equal(ActionCatalogReader.Official().Length, entries.Count(e => e.Kind == "action"));
        Assert.All(entries.Where(e => e.Kind == "action"), e => Assert.Equal("official", e.Action!.Source));
        foreach ((string kind, int count) in BatchValues.ExpectedCounts(entries))
        { TestContext.Current.TestOutputHelper!.WriteLine($"Expected {kind}: {count}"); }
        TestContext.Current.TestOutputHelper!.WriteLine($"Unique API groups: {entries.Where(e => e.Kind != "action").Select(e => e.Group).Distinct(StringComparer.Ordinal).Count()}");
    }
}
