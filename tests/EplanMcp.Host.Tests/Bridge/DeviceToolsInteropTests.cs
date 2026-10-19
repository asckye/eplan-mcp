using System.IO.Pipes;
using System.Text.Json;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Server;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Bridge;

/// <summary>
/// The device tools through the real BridgeClient, pipe and Bridge.Core operations; only the EPLAN adapters are fake
/// (<see cref="SampleDevices"/>).
/// </summary>
[Trait("Size", "Medium")]
public sealed class DeviceToolsInteropTests : IAsyncDisposable
{
    private readonly string _prefix = "EplanMcpTest." + Guid.NewGuid().ToString("N") + ".";
    private readonly SampleDevices _devices = new();
    private readonly BridgeServer _server;
    private readonly BridgeClient _client;

    public DeviceToolsInteropTests()
    {
        var catalog = new Catalog();
        var router = new OperationRouter(
            [new ProjectListOperation(catalog), new DeviceListOperation(catalog, _devices), new DeviceGetOperation(catalog, _devices)],
            new InlineDispatcher(),
            null,
            new NullLog(),
            TimeSpan.FromSeconds(10));
        _server = new BridgeServer(
            _prefix + Environment.ProcessId + ".00000000000000d1",
            name => new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly),
            new BridgeServerOptions { BridgeVersion = "devices", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3.25421" } },
            router,
            new NullLog());
        _server.Start();
        _client = new BridgeClient(new PipeBridgeDiscovery(_prefix), new PipeBridgeConnector(), NullLogger<BridgeClient>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        _server.Dispose();
    }

    [Fact]
    public async Task DeviceList_ShortTag_FindsItInEveryStructure()
    {
        CallToolResult result = await List(tagContains: "-FC1");

        JsonElement data = Structured(result);
        Assert.Equal("Line1", data.GetProperty("project").GetString());
        Assert.Equal(["=GAA+A1-FC1", "=GAB1+A1-FC1"], Tags(data));
        Assert.Equal(["-FC1", "-FC1"], data.GetProperty("devices").EnumerateArray().Select(d => d.GetProperty("visibleTag").GetString()));
        Assert.Equal("Listed 2 devices.", ((TextContentBlock)result.Content[0]).Text);
    }

    [Fact]
    public async Task DeviceList_CategoryAndHasPart_FindMotorsWithoutParts()
    {
        Assert.Equal(["=GAA+A1-MA1"], Tags(Structured(await List(category: "motor", hasPart: false))));
    }

    [Fact]
    public async Task DeviceList_PageWithAuxiliaryFunctions_ListsEverythingNamedOnThatPage()
    {
        JsonElement data = Structured(await List(page: "=GAA+A1&EFS1/1", includeAuxiliary: true));

        Assert.Equal(["=GAA+A1-FC1", "=GAA+A1-PF1:1"], Tags(data));
        Assert.Equal([true, false], data.GetProperty("devices").EnumerateArray().Select(d => d.GetProperty("main").GetBoolean()));
        Assert.Equal(JsonValueKind.Null, data.GetProperty("devices")[1].GetProperty("visibleTag").ValueKind);
    }

    [Fact]
    public async Task DeviceList_Pages_CoverEveryNamedMainFunctionOnce()
    {
        var seen = new List<string?>();
        string cursor = string.Empty;
        int pages = 0;
        do
        {
            JsonElement data = Structured(await List(limit: 2, cursor: cursor));
            seen.AddRange(Tags(data));
            Assert.Equal(6, data.GetProperty("total").GetInt32());
            cursor = data.GetProperty("nextCursor").GetString() ?? string.Empty;
            pages++;
        }
        while (cursor.Length > 0);

        Assert.Equal(["=GAA+A1-FC1", "=GAB1+A1-FC1", "+A1-FC2", "=+-CU1", "=GAA+A1-MA1", "=GAA+A1-MA2"], seen);
        Assert.Equal(3, pages);
    }

    [Fact]
    public async Task DeviceList_MarkersInTheShownTag_AreRemoved()
    {
        JsonElement device = Structured(await List(tagContains: "cu1")).GetProperty("devices")[0];

        Assert.Equal("=+-CU1", device.GetProperty("visibleTag").GetString());
    }

    [Fact]
    public async Task DeviceList_UnknownPageAndCategory_AreBusinessErrors()
    {
        CallToolResult page = await List(page: "=GAA+A1&EFS1/99");
        CallToolResult category = await List(category: "Contactor");

        Assert.Equal(ErrorReason.PageNotFound, Reason(page));
        Assert.Equal(ErrorReason.InvalidArgument, Reason(category));
    }

    [Fact]
    public async Task DeviceGet_FullTag_ReturnsTheDeviceWithItsAuxiliaryFunctionsAndParts()
    {
        CallToolResult result = await Get("=GAA+A1-FC1");

        JsonElement data = Structured(result);
        JsonElement device = data.GetProperty("device");
        Assert.Equal(("-FC1", "=GAA+A1&EFS1/1", "功能文本"), (device.GetProperty("visibleTag").GetString(), device.GetProperty("page").GetString(), device.GetProperty("text").GetString()));
        Assert.Equal(["1", "2"], device.GetProperty("pins").EnumerateArray().Select(p => p.GetString()));
        JsonElement contact = Assert.Single(data.GetProperty("functions").EnumerateArray());
        Assert.Equal(("NoContact", "=GAA+A1&EFS1/2"), (contact.GetProperty("category").GetString(), contact.GetProperty("page").GetString()));
        Assert.Equal(2, data.GetProperty("parts").GetArrayLength());
        Assert.Equal("Device found: CircuitBreaker, 1 auxiliary function, 2 parts.", ((TextContentBlock)result.Content[0]).Text);
    }

    [Fact]
    public async Task DeviceGet_ShortTagInTwoStructures_IsAmbiguousWithBothFullTags()
    {
        CallToolResult result = await Get("-FC1");

        Assert.Equal(ErrorReason.AmbiguousName, Reason(result));
        JsonElement metadata = JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement.GetProperty("error").GetProperty("metadata");
        Assert.Equal("=GAA+A1-FC1\n=GAB1+A1-FC1", metadata.GetProperty("candidates").GetString());
    }

    [Fact]
    public async Task DeviceGet_UniqueShownTag_FindsTheDevice()
    {
        Assert.Equal("=GAA+A1-MA2", Structured(await Get("-ma2")).GetProperty("device").GetProperty("tag").GetString());
    }

    [Fact]
    public async Task DeviceGet_DetailedInALanguage_ReturnsTheProperties()
    {
        JsonElement property = Structured(await Get("=GAA+A1-MA1", ToolArguments.Detailed, "en_US")).GetProperty("properties")[0];

        Assert.Equal((20011, "en_US"), (property.GetProperty("id").GetInt32(), property.GetProperty("value").GetString()));
    }

    [Fact]
    public async Task DeviceGet_UnknownTag_IsDeviceNotFound()
    {
        Assert.Equal(ErrorReason.DeviceNotFound, Reason(await Get("-K99")));
    }

    private Task<CallToolResult> Get(string tag, string responseFormat = ToolArguments.Concise, string language = "") =>
        DeviceTools.DeviceGetAsync(_client, tag, string.Empty, responseFormat, language, TestContext.Current.CancellationToken);

    private Task<CallToolResult> List(
        string tagContains = "",
        string page = "",
        string category = "",
        bool? hasPart = null,
        bool includeAuxiliary = false,
        int limit = DeviceTools.DefaultLimit,
        string cursor = "") =>
        DeviceTools.DeviceListAsync(_client, string.Empty, tagContains, page, category, hasPart, includeAuxiliary, limit, cursor, TestContext.Current.CancellationToken);

    private static JsonElement Structured(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        return result.StructuredContent!.Value;
    }

    private static string?[] Tags(JsonElement data) => [.. data.GetProperty("devices").EnumerateArray().Select(d => d.GetProperty("tag").GetString())];

    private static string? Reason(CallToolResult result)
    {
        Assert.True(result.IsError);
        return JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement.GetProperty("error").GetProperty("reason").GetString();
    }

    private sealed class Catalog : IProjectCatalog
    {
        public IReadOnlyList<OpenProject> ListOpenProjects() => [new OpenProject { Name = "Line1", Path = @"D:\Projects\Line1", Current = true }];
    }

    private sealed class InlineDispatcher : IMainThreadDispatcher
    {
        public DispatchResult Run(Func<object?> work, TimeSpan startTimeout)
        {
            try
            {
                return DispatchResult.Completed(work());
            }
            catch (Exception ex)
            {
                return DispatchResult.Faulted(ex);
            }
        }
    }

    private sealed class NullLog : IBridgeLog
    {
        public void Write(BridgeLogLevel level, string message, Exception? exception = null)
        {
        }
    }
}
