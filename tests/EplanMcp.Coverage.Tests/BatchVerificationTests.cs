extern alias host;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Large.Tests;
using host::EplanMcp.Host.Bridge;
using host::EplanMcp.Host.Tools;

namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Small")]
public sealed class BatchVerificationTests
{
    [Fact]
    public void ExpectedCountsMatchTheGeneratedPropertyAndMethodTables()
    {
        string[] actions = ["TestAction", "TestAction"];
        BatchEntry[] entries = BatchValues.Entries(actions);
        Dictionary<string, int> expected = BatchValues.ExpectedCounts(entries);
        string[] properties = [.. ApiTryCatalog.PropertyGroups.Where(g => ApiTryCatalog.MethodAccess(g) is null)];
        string[] methods = ApiTryCatalog.MethodGroups;
        Assert.Equal(properties.Length + methods.Count(m => ApiTryCatalog.MethodAccess(m) == "read"), expected["api-read"]);
        Assert.Equal(properties.Count(p => ApiTryCatalog.ValueShape(p) is not null) + methods.Count(m => ApiTryCatalog.MethodAccess(m) == "write"), expected["api-write"]);
        Assert.Equal(1, expected["action"]);
        Assert.Equal(properties.Order(StringComparer.Ordinal), entries.Where(e => !e.Method && e.Kind == "api-read").Select(e => e.Group).Order(StringComparer.Ordinal));
        Assert.Equal(methods.Order(StringComparer.Ordinal), entries.Where(e => e.Method).Select(e => e.Group).Order(StringComparer.Ordinal));
        Assert.Equal(actions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(a => "A:" + a), entries.Where(e => e.Kind == "action").Select(e => e.Id));
        Assert.Equal(entries.Length, entries.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count());
        foreach (BatchEntry entry in entries.Where(e => e.Method))
        { Assert.NotNull(ApiTryCatalog.Shape(entry.Group, entry.Parameters!.Keys)); }
        foreach ((string kind, int count) in expected)
        { TestContext.Current.TestOutputHelper!.WriteLine($"Expected {kind}: {count}"); }
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
public sealed class BatchActionCatalogTests
{
    [Fact]
    public async Task CountEligibleInstalledActionsWithoutConnectingToEplanAsync()
    {
        if (!OperatingSystem.IsWindows())
        { return; }
        ActionCatalogData catalog = await new ActionCatalogReader().ReadAsync(TestContext.Current.CancellationToken);
        var actions = new List<string>();
        foreach (ActionCatalogItem item in catalog.Items)
        {
            if (item.Type is not null || item.Parameters.Length != 0)
            { continue; }
            try
            { ActionTryTools.Check(catalog, "scratch", item.Name, null, default); }
            catch (BridgeCallException) { continue; }
            actions.Add(item.Name);
        }
        BatchEntry[] entries = BatchValues.Entries(actions);
        Assert.Equal(actions.Distinct(StringComparer.Ordinal).Count(), entries.Count(e => e.Kind == "action"));
        foreach ((string kind, int count) in BatchValues.ExpectedCounts(entries))
        { TestContext.Current.TestOutputHelper!.WriteLine($"Expected {kind}: {count}"); }
        TestContext.Current.TestOutputHelper!.WriteLine($"Unique API groups: {entries.Where(e => e.Kind != "action").Select(e => e.Group).Distinct(StringComparer.Ordinal).Count()}");
    }
}
