using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
public sealed class ContextToolTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-context-tool-").FullName);
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 10, 23, 8, 30, 0, TimeSpan.Zero));
    private readonly FakeBridgeClient _bridge = new() { Status = new([], new() { Operations = [OperationNames.ProjectSnapshot] }, null) };
    private PackLibrary Packs => new(_locations, HostConfig.Defaults(_locations), new AuditLog(_locations.Audit, _time), _time);
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public ContextToolTests() => _bridge.Handlers[OperationNames.ProjectSnapshot] = args => ContextTestSnapshot.Data((ProjectSnapshotArgs)args!);

    [Fact]
    public async Task ExplicitNonDefaultCabinet_IsCapabilityMissingBeforeBridge()
    {
        Assert.Equal(ErrorReason.CapabilityMissing, Error(await ContextTools.GetAsync(_bridge, Packs, Parse("{}"), cabinet: "panel", cancellationToken: Token)).GetProperty("reason").GetString());
        Assert.Empty(_bridge.Calls);
    }

    [Fact]
    public async Task InvalidFields_AreReportedTogetherBeforeAnyRead()
    {
        JsonElement error = Error(await Get("{\"pages\":[],\"structures\":[\"+A*\"],\"kinds\":[\"device\"],\"locators\":[{\"extra\":true}],\"depth\":1}", "[\"objects\",\"objects\",\"unknown\"]", 0));
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        string violations = error.GetProperty("metadata").GetProperty("violations").GetString()!;
        foreach (string field in new[] { "/pages", "/structures", "/kinds", "/locators", "/depth", "/include", "/max_tokens" })
        {
            Assert.Contains(field, violations, StringComparison.Ordinal);
        }
        Assert.Empty(_bridge.Calls);
    }

    [Theory]
    [InlineData("null", "/scope must be an object")]
    [InlineData("[]", "/scope must be an object")]
    [InlineData("{\"pages\":true}", "/scope/pages must be an array")]
    [InlineData("{\"pages\":[\"\"]}", "/scope/pages/0 must be a non-empty string")]
    [InlineData("{\"kinds\":[\"device\"]}", "/scope/kinds/0 must be one of")]
    [InlineData("{\"structures\":[\"+A*/1\"]}", "/scope/structures/0 must contain complete structure segments")]
    [InlineData("{\"locators\":[null]}", "/scope/locators/0 must be an object locator")]
    [InlineData("{\"locators\":[{}]}", "/scope/locators/0/project is required")]
    [InlineData("{\"locators\":[{\"type\":0,\"oid\":1.5}]}", "/scope/locators/0/type must be the positive integer")]
    [InlineData("{\"locators\":[{\"project\":false}]}", "/scope/locators/0/project must be a non-empty string")]
    [InlineData("{\"locators\":[{\"kind\":\"device\"}]}", "/scope/locators/0/kind must be page, function or connection")]
    [InlineData("{\"locators\":[{\"pos\":\"10 20\"}]}", "/scope/locators/0/pos must be 'x,y'")]
    [InlineData("{\"locators\":[{\"other\":1}]}", "/scope/locators/0/other is unknown")]
    public async Task InvalidScope_HasAnActionableFieldMessageBeforeReading(string scope, string expected)
    {
        JsonElement error = Error(await Get(scope));
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Contains(expected, error.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
        Assert.Empty(_bridge.Calls);
    }

    [Theory]
    [InlineData("null", "/include must be an array")]
    [InlineData("[]", "/include must contain at least one item")]
    [InlineData("[1]", "/include/0 must be a non-empty string")]
    [InlineData("[\"objects\",\"objects\"]", "/include/1 duplicates an earlier item")]
    [InlineData("[\"unknown\"]", "/include/0 must be one of")]
    public async Task InvalidInclude_HasAnActionableFieldMessageBeforeReading(string include, string expected)
    {
        JsonElement error = Error(await Get(include: include));
        Assert.Contains(expected, error.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
        Assert.Empty(_bridge.Calls);
    }

    [Fact]
    public async Task MoreThan200Locators_IsRejectedBeforeReading()
    {
        string scope = JsonSerializer.Serialize(new
        {
            locators = Enumerable.Range(1, 201).Select(oid => new
            {
                project = ContextTestSnapshot.ProjectPath,
                kind = "function",
                type = 17,
                oid,
            }),
        });
        JsonElement error = Error(await Get(scope));
        Assert.Contains("/scope/locators must contain at least one item and at most 200", error.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
        Assert.Empty(_bridge.Calls);
    }

    [Fact]
    public async Task NullableLocatorChecks_AreAccepted()
    {
        string scope = JsonSerializer.Serialize(new
        {
            locators = new List<ObjectLocator>
            {
                new() { Project = ContextTestSnapshot.ProjectPath, Kind = "function", Type = 17, Oid = 29 },
            },
        }, HostJson.Options);
        JsonElement result = Data(await Get(scope));
        Assert.Single(result.GetProperty("objects").EnumerateArray());
    }

    private Task<CallToolResult> Get(string scope = "{}", string? include = null, int budget = 20000) =>
        ContextTools.GetAsync(_bridge, Packs, Parse(scope), include: include is null ? (JsonElement?)null : Parse(include), max_tokens: budget, cancellationToken: Token);

    internal static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    internal static JsonElement Data(CallToolResult call)
    {
        ToolResultAssert.Succeeded(call);
        return call.StructuredContent!.Value;
    }

    internal static JsonElement Error(CallToolResult call)
    {
        Assert.True(call.IsError);
        return (call.StructuredContent ?? Parse(((TextContentBlock)call.Content[0]).Text)).GetProperty("error");
    }

    internal static void AssertSources(JsonElement result) => ContextBuilderReadTests.AssertSources(result);

    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);
}
