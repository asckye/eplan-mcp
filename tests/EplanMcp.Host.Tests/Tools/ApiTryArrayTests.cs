using System.Text.Json;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;
namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class ApiTryArrayTests
{
    [Theory]
    [InlineData("System.Boolean", "[true,false]", true)]
    [InlineData("System.Int32", "[2147483648]", false)]
    [InlineData("System.Int64", "[9223372036854775807]", true)]
    [InlineData("System.Int64", "[9223372036854775808]", false)]
    [InlineData("System.Double", "[1,2.5]", true)]
    [InlineData("System.Double", "[1e999]", false)]
    [InlineData("enum", "[\"One\"]", true)]
    [InlineData("enum", "[\"one\"]", false)]
    [InlineData("Eplan.EplApi.Base.PointD", "[{\"x\":1,\"y\":2}]", true)]
    [InlineData("Eplan.EplApi.Base.PointD", "[{\"x\":1,\"y\":1e999}]", false)]
    [InlineData("Eplan.EplApi.Base.PointD", "[{\"x\":1,\"x\":2,\"y\":3}]", false)]
    public void EveryValueElementIsValidatedWithoutCoercion(string type, string json, bool valid) => Assert.Equal(valid,
        ApiTryTools.ValidValue(type == "enum" ? ["array", "enum", "One", "Two"] : ["array", type], JsonSerializer.Deserialize<JsonElement>(json)));
    private static object Locator(string project = "Scratch") => new { project, page = "=A/1", type = 1, oid = 1, digest = new string('a', 64) };
    [Theory]
    [InlineData(0, true)]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public void ArraysHaveAHardElementLimit(int count, bool valid) => Assert.Equal(valid, ApiTryTools.MethodParameters("O:Eplan.EplApi.DataModel.Group.InsertSubPlacements",
        JsonSerializer.SerializeToElement(new { target = Locator(), placements = Enumerable.Range(0, count).Select(_ => Locator()).ToArray() })));
    [Theory]
    [InlineData("[null]")]
    [InlineData("[[1]]")]
    [InlineData("[{}]")]
    [InlineData("[123]")]
    public void InvalidLocatorElementsCannotCoerce(string json) => Assert.False(ApiTryTools.MethodParameters("O:Eplan.EplApi.DataModel.Group.InsertSubPlacements",
        JsonSerializer.Deserialize<JsonElement>("{\"target\":" + JsonSerializer.Serialize(Locator()) + ",\"placements\":" + json + "}")));
    [Fact]
    public void ScalarCollectionsStillEnforceTheExactElementType()
    {
        const string Group = "O:Eplan.EplApi.DataModel.Project.SetSortedLocations";
        Assert.True(ApiTryTools.MethodParameters(Group, JsonSerializer.SerializeToElement(new { target = Locator(), type = "Location", arrLocations = (string[])["A", "B"] })));
        Assert.False(ApiTryTools.MethodParameters(Group, JsonSerializer.SerializeToElement(new { target = Locator(), type = "Location", arrLocations = new object[] { "A", 1 } })));
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    [Fact]
    public void ResultsPageByBudgetWithoutReexecution_AndBindQueryAndHost()
    {
        var clock = new Clock();
        var pages = new ApiTryPages(clock);
        string json = JsonSerializer.Serialize(Enumerable.Range(0, 200).Select(i => new { project = "Scratch", page = new string('a', 200), type = 1, oid = i + 1, digest = new string('a', 64) }));
        var first = Assert.IsType<ApiTryResultPage>(pages.Shape(json, "q"));
        Assert.Equal(200, first.Total);
        Assert.True(first.Truncated);
        Assert.NotNull(first.NextCursor);
        Assert.True(ResultShaper.Fits(first, ResultShaper.DefaultTokenBudget));
        var all = first.Items.ToList();
        ApiTryResultPage current = first;
        while (current.NextCursor is not null)
        {
            current = pages.Read(current.NextCursor, "q");
            all.AddRange(current.Items);
        }
        Assert.Equal(Enumerable.Range(1, 200), all.Select(v => v.GetProperty("oid").GetInt32()));
        Assert.Throws<BridgeCallException>(() => pages.Read(first.NextCursor!, "different"));
        Assert.Throws<BridgeCallException>(() => new ApiTryPages(clock).Read(first.NextCursor!, "q"));
        clock.Now += TimeSpan.FromMinutes(11);
        Assert.Throws<BridgeCallException>(() => pages.Read(first.NextCursor!, "q"));
    }
    [Fact]
    public void StructuredValuesAreCleanedAndLocatorIdentitySurvives()
    {
        var pages = new ApiTryPages(new Clock());
        var array = Assert.IsType<ApiTryResultPage>(pages.Shape("[\"a\\u202eb\",2,true,{\"x\":1,\"y\":2}]", "q"));
        Assert.Equal("ab", array.Items[0].GetString());
        Assert.Equal(2, array.Items[1].GetInt32());
        Assert.True(array.Items[2].GetBoolean());
        Assert.Equal(1, array.Items[3].GetProperty("x").GetInt32());
        JsonElement locator = JsonSerializer.SerializeToElement(Locator("Scratch\u202e"));
        Assert.Equal(locator.GetRawText(), Assert.IsType<JsonElement>(pages.Shape(locator.GetRawText(), "q")).GetRawText());
        Assert.IsType<ApiTryResultPage>(pages.Shape("[]", "q"));
        var outputs = Assert.IsType<Dictionary<string, object?>>(pages.Shape("{\"returnValue\":null,\"outputs\":{\"points\":[{\"x\":1,\"y\":2}]}}", "q"));
        Assert.IsType<ApiTryResultPage>(Assert.IsType<Dictionary<string, object?>>(outputs["outputs"])["points"]);
    }
    [Fact]
    public void CacheEvictionIsBounded_AndMalformedCursorsFail()
    {
        var pages = new ApiTryPages(new Clock());
        string json = JsonSerializer.Serialize(Enumerable.Repeat(new string('x', 500), 200));
        string cursor = Assert.IsType<ApiTryResultPage>(pages.Shape(json, "q")).NextCursor!;
        for (int i = 0; i < 16; i++)
        { pages.Shape(json, "q"); }
        Assert.Throws<BridgeCallException>(() => pages.Read(cursor, "q"));
        foreach (string invalid in new[] { "", "a.b.c", "a.%%%%", new string('x', 1025) })
        { Assert.Throws<BridgeCallException>(() => pages.Read(invalid, "q")); }
    }
    [Fact]
    public void SeveralNamedCollectionsShareOneResultBudget()
    {
        var pages = new ApiTryPages(new Clock());
        string[] items = Enumerable.Repeat(new string('x', 500), 200).ToArray();
        object? shaped = pages.Shape(JsonSerializer.Serialize(new { returnValue = items, outputs = new { a = items, b = items } }), "q");
        Assert.True(ResultShaper.Fits(shaped!, ResultShaper.DefaultTokenBudget));
    }
}
