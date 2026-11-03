extern alias host;
using System.Runtime.Versioning;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Large.Tests;
using host::EplanMcp.Host.Tools;

namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Small")]
[SupportedOSPlatform("windows")]
public sealed class BatchReadRecipeTests
{
    [Fact]
    public async Task ParsedRecipeSelectsItsDeclaredSource()
    {
        BatchReadRecipes recipes = BatchReadRecipes.Parse("""{"Eplan.EplApi.DataModel.Function":"project"}""");
        BatchArguments result = await recipes.ArgumentsAsync(Entry(["Eplan.EplApi.DataModel.Function"]), (source, kinds) =>
        {
            Assert.Equal("project", source);
            Assert.Contains("Function", kinds);
            return Task.FromResult<JsonElement?>(Locator());
        });
        Assert.NotNull(result.Values);
        Assert.Null(result.Outcome);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"enum\":")]
    public void MalformedRecipesAreRejected(string json) => Assert.ThrowsAny<JsonException>(() => BatchReadRecipes.Parse(json));

    [Theory]
    [InlineData("{\"enum\":\"unknown\"}")]
    [InlineData("{\"enum\":null}")]
    [InlineData("{\"enum\":\"enum\",\"enum\":\"enum\"}")]
    public void UnknownOrDuplicateSourcesAreRejected(string json) => Assert.Throws<FormatException>(() => BatchReadRecipes.Parse(json));

    [Theory]
    [InlineData("Eplan.EplApi.DataModel.Project", "project", "Project")]
    [InlineData("Eplan.EplApi.DataModel.Page", "circuit-page", "Page")]
    [InlineData("Eplan.EplApi.DataModel.Function", "locator", "Function")]
    [InlineData("Eplan.EplApi.DataModel.FunctionBase", "locator", "Function")]
    [InlineData("Eplan.EplApi.DataModel.Placement", "locator", "Placement")]
    [InlineData("Eplan.EplApi.DataModel.StorableObject", "locator", "Function")]
    [InlineData("Eplan.EplApi.DataModel.Connection", "locator", "Connection")]
    [InlineData("Eplan.EplApi.DataModel.E3D.Placement3D", "locator", "Placement3D")]
    [InlineData("Eplan.EplApi.DataModel.MasterData.SymbolLibrary", "locator", "SymbolLibrary")]
    public async Task EveryLocatorRecipeUsesDetachedDiscovery(string type, string expectedSource, string expectedKind)
    {
        JsonElement locator = Locator();
        BatchArguments result = await BatchReadRecipes.Default.ArgumentsAsync(Entry([type]), (source, kinds) =>
        {
            Assert.Equal(expectedSource, source);
            Assert.Contains(expectedKind, kinds);
            Assert.All(kinds, kind => Assert.True(ObjectReadTypes.Supported(kind)));
            return Task.FromResult<JsonElement?>(locator);
        });
        Assert.Equal(locator, Assert.Single(result.Values!).Value);
        Assert.Null(result.Outcome);
    }

    [Fact]
    public async Task EnumRecipeUsesOnlyAPublishedMemberWithoutDiscovery()
    {
        BatchArguments result = await BatchReadRecipes.Default.ArgumentsAsync(Entry(["enum", "First", "Second"]), UnexpectedLookup);
        Assert.Equal("First", Assert.Single(result.Values!).Value);
        Assert.Null(result.Outcome);
    }

    [Fact]
    public async Task ObjectArrayRecipeUsesANonemptyDiscoveredElement()
    {
        JsonElement locator = Locator();
        BatchArguments result = await BatchReadRecipes.Default.ArgumentsAsync(Entry(["array", "Eplan.EplApi.DataModel.StorableObject"]),
            (_, _) => Task.FromResult<JsonElement?>(locator));
        Assert.Equal(locator, Assert.Single(Assert.IsType<object[]>(Assert.Single(result.Values!).Value)));
        Assert.Null(result.Outcome);
    }

    [Theory]
    [InlineData("System.String")]
    [InlineData("System.Boolean")]
    [InlineData("System.Int32")]
    [InlineData("Eplan.EplApi.Base.PointD")]
    [InlineData("unknown")]
    public async Task UnknownTypesRetainNeedsContextWithoutLookingUpValues(string type)
    {
        BatchEntry entry = Entry([type]);
        Assert.False(BatchReadRecipes.Default.CanBuild(entry));
        BatchArguments result = await BatchReadRecipes.Default.ArgumentsAsync(entry, UnexpectedLookup);
        Assert.Null(result.Values);
        Assert.Equal("failed:needs_context", result.Outcome);
    }

    [Theory]
    [InlineData("Eplan.EplApi.DataModel.Function")]
    [InlineData("Eplan.EplApi.DataModel.Page")]
    [InlineData("Eplan.EplApi.DataModel.Project")]
    public async Task KnownRecipeWithoutAValueHasItsOwnOutcome(string type)
    {
        BatchEntry entry = Entry([type]);
        Assert.True(BatchReadRecipes.Default.CanBuild(entry));
        BatchArguments result = await BatchReadRecipes.Default.ArgumentsAsync(entry, (_, _) => Task.FromResult<JsonElement?>(null));
        Assert.Null(result.Values);
        Assert.Equal("skipped:no_context_value", result.Outcome);
        Assert.Contains("value", result.Detail, StringComparison.Ordinal);
        Assert.False(BatchValues.Stops(new(entry.Id, entry.Kind) { Outcome = result.Outcome! }));
    }

    [Fact]
    public async Task HandlesAndWritesCannotAcquireReadRecipeValues()
    {
        BatchEntry[] entries =
        [
            Entry(["handle", "Eplan.EplApi.DataModel.AnyPropertyId"]),
            Entry(["array", "handle", "Eplan.EplApi.DataModel.AnyPropertyId"]),
            Entry(["Eplan.EplApi.DataModel.Function"]) with { ObjectKinds = [] },
            Entry(["Eplan.EplApi.DataModel.Function"]) with { Kind = "api-write" },
        ];
        foreach (BatchEntry entry in entries)
        {
            Assert.False(BatchReadRecipes.Default.CanBuild(entry));
            BatchArguments result = await BatchReadRecipes.Default.ArgumentsAsync(entry, UnexpectedLookup);
            Assert.Equal("failed:needs_context", result.Outcome);
        }
    }

    [Fact]
    public async Task PageBoundReadSelectsAnExplicitPageAndResolvesItBeforeTheFunction()
    {
        const string group = "O:Eplan.EplApi.HEServices.NameService.CanDeviceTagBeNested";
        BatchEntry entry = Assert.Single(BatchValues.ApiEntries(), item => item.Group == group);
        Assert.Equal(["pPage", "pFunction"], entry.Parameters!.Keys);
        var calls = new List<string>();
        BatchArguments result = await BatchReadRecipes.Default.ArgumentsAsync(entry, (source, _) =>
        {
            calls.Add(source);
            return Task.FromResult<JsonElement?>(Locator());
        });
        Assert.Equal(["circuit-page", "locator"], calls);
        Assert.NotNull(result.Values);
        Assert.Null(result.Outcome);
    }

    [Fact]
    public async Task AnUnplacedFunctionDoesNotSatisfyThePagePrecondition()
    {
        BatchEntry entry = Assert.Single(BatchValues.ApiEntries(), item => item.Group == "O:Eplan.EplApi.HEServices.NameService.CanDeviceTagBeNested");
        BatchArguments result = await BatchReadRecipes.Default.ArgumentsAsync(entry, (source, _) =>
            Task.FromResult<JsonElement?>(Locator(source == "circuit-page" ? "=A/1" : "")));
        Assert.Null(result.Values);
        Assert.Equal("skipped:no_context_value", result.Outcome);
        Assert.Contains("placed object", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyEnumCannotFabricateAValue()
    {
        BatchArguments result = await BatchReadRecipes.Default.ArgumentsAsync(Entry(["enum"]), UnexpectedLookup);
        Assert.Null(result.Values);
        Assert.Equal("skipped:no_context_value", result.Outcome);
    }

    [Fact]
    public async Task EveryRecipeSupportedReadMethodProducesThePublishedParameterShape()
    {
        BatchEntry[] entries = [.. BatchValues.ApiEntries().Where(entry => entry.Method && BatchReadRecipes.Default.CanBuild(entry))];
        Assert.NotEmpty(entries);
        foreach (BatchEntry entry in entries)
        {
            BatchArguments result = await BatchReadRecipes.Default.ArgumentsAsync(entry, (_, _) => Task.FromResult<JsonElement?>(Locator()));
            Assert.True(result.Values is not null, entry.Id + ": " + result.Detail);
            if (entry.ObjectKinds is not null)
            { result.Values!["target"] = Locator(); }
            Assert.True(ApiTryTools.MethodParameters(entry.Group, JsonSerializer.SerializeToElement(result.Values)), entry.Signature);
        }
        TestContext.Current.TestOutputHelper!.WriteLine($"Recipe-supported read methods: {entries.Length}");
    }

    private static BatchEntry Entry(string[] shape) => new("test", "api-read", Parameters: new(StringComparer.Ordinal) { ["value"] = shape });
    private static Task<JsonElement?> UnexpectedLookup(string _, string[] __) => throw new InvalidOperationException("No discovery should be required.");
    private static JsonElement Locator(string page = "=A/1") => JsonSerializer.SerializeToElement(new
    { project = "scratch", page, type = 1, oid = 2, digest = new string('a', 64) });
}
