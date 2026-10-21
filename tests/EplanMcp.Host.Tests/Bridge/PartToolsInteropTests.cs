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
/// eplan_part_list through the real BridgeClient, pipe and Bridge.Core operation; only the EPLAN adapter is fake
/// (<see cref="SampleParts"/>).
/// </summary>
[Trait("Size", "Medium")]
public sealed class PartToolsInteropTests : IAsyncDisposable
{
    private readonly string _prefix = "EplanMcpTest." + Guid.NewGuid().ToString("N") + ".";
    private readonly SamplePartsDatabase _database = new();
    private readonly BridgeServer _server;
    private readonly BridgeClient _client;

    public PartToolsInteropTests()
    {
        var catalog = new Catalog();
        var router = new OperationRouter(
            [new ProjectListOperation(catalog), new PartListOperation(catalog, new SampleParts()), new PartSearchOperation(_database)],
            new InlineDispatcher(),
            null,
            new NullLog(),
            TimeSpan.FromSeconds(10));
        _server = new BridgeServer(
            _prefix + Environment.ProcessId + ".00000000000000a1",
            name => new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly),
            new BridgeServerOptions { BridgeVersion = "parts", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3.25421" } },
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
    public async Task PartList_SumsUpEachPartAndJoinsTheProjectsPartData()
    {
        CallToolResult result = await List();

        JsonElement data = Structured(result);
        Assert.Equal("Line1", data.GetProperty("project").GetString());
        Assert.Equal(["LAPP.4510011", "RIT.8601000", "SIE.3RT2016-1BB41", "SIE.3RV2011-1JA10"], Numbers(data));
        JsonElement wire = Part(data, "LAPP.4510011");
        Assert.Equal((3, 0, 2), (wire.GetProperty("quantity").GetInt32(), wire.GetProperty("devices").GetInt32(), wire.GetProperty("connections").GetInt32()));
        Assert.Equal(JsonValueKind.Null, wire.GetProperty("description").ValueKind);
        JsonElement contactor = Part(data, "SIE.3RT2016-1BB41");
        Assert.Equal((3, 2, 0), (contactor.GetProperty("quantity").GetInt32(), contactor.GetProperty("devices").GetInt32(), contactor.GetProperty("connections").GetInt32()));
        Assert.Equal(("Contactor, AC-3, 4 kW/400 V", "SIE"), (contactor.GetProperty("description").GetString(), contactor.GetProperty("manufacturer").GetString()));
        Assert.Equal("2", Part(data, "RIT.8601000").GetProperty("variant").GetString());
        Assert.Equal("Listed 4 parts.", ((TextContentBlock)result.Content[0]).Text);
    }

    [Fact]
    public async Task PartList_ManufacturerAndText_FilterIgnoringCaseAndCombine()
    {
        Assert.Equal(["SIE.3RT2016-1BB41", "SIE.3RV2011-1JA10"], Numbers(Structured(await List(manufacturer: "sie"))));
        Assert.Equal(["SIE.3RV2011-1JA10"], Numbers(Structured(await List(text: "BREAKER"))));
        Assert.Equal(["SIE.3RT2016-1BB41"], Numbers(Structured(await List(manufacturer: "SIE", text: "3rt"))));
        Assert.Empty(Numbers(Structured(await List(manufacturer: "Siemens"))));
    }

    [Fact]
    public async Task PartList_Language_ReadsDescriptionsInItAndAnUnknownOneIsInvalid()
    {
        JsonElement chinese = Structured(await List(text: "接触器", language: "zh_CN"));
        CallToolResult unknown = await List(language: "xx_XX");

        Assert.Equal("接触器，AC-3，4 kW/400 V", Part(chinese, "SIE.3RT2016-1BB41").GetProperty("description").GetString());
        Assert.True(unknown.IsError);
        Assert.Contains(ErrorReason.InvalidArgument, ((TextContentBlock)unknown.Content[0]).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PartList_PagesThroughEveryPartInOrder()
    {
        var numbers = new List<string>();
        string cursor = string.Empty;
        do
        {
            JsonElement data = Structured(await List(limit: 1, cursor: cursor));
            numbers.AddRange(Numbers(data));
            cursor = data.GetProperty("nextCursor").GetString() ?? string.Empty;
        }
        while (cursor.Length > 0);

        Assert.Equal(["LAPP.4510011", "RIT.8601000", "SIE.3RT2016-1BB41", "SIE.3RV2011-1JA10"], numbers);
    }

    [Fact]
    public async Task PartSearch_FindsEachVariantInOrder_AndNamesTheDatabase()
    {
        CallToolResult result = await PartTools.PartSearchAsync(_client, "3rh21", "sie", string.Empty, PartTools.DefaultSearchLimit, string.Empty, TestContext.Current.CancellationToken);

        JsonElement data = Structured(result);
        Assert.Equal(@"$(MD_PARTS)\EPLAN_parts.alk", data.GetProperty("database").GetString());
        Assert.Equal(
            [("SIE.3RH2122-1HB40", "1", true), ("SIE.3RH2122-1HB40", "2", false)],
            data.GetProperty("parts").EnumerateArray().Select(p => (p.GetProperty("partNumber").GetString(), p.GetProperty("variant").GetString(), p.GetProperty("hasMacro").GetBoolean())));
        Assert.Equal("Found 2 parts.", ((TextContentBlock)result.Content[0]).Text);
    }

    [Fact]
    public async Task PartSearch_PagesThroughTheMatches_AndReadsDescriptionsInTheLanguage()
    {
        var numbers = new List<string>();
        string cursor = string.Empty;
        do
        {
            JsonElement data = Structured(await PartTools.PartSearchAsync(_client, "接触器", string.Empty, "zh_CN", 1, cursor, TestContext.Current.CancellationToken));
            numbers.AddRange(Numbers(data));
            cursor = data.GetProperty("nextCursor").GetString() ?? string.Empty;
        }
        while (cursor.Length > 0);

        Assert.Equal(["SIE.3RH2122-1HB40", "SIE.3RH2122-1HB40", "SIE.3RT2016-1BB41"], numbers);
    }

    [Fact]
    public async Task PartSearch_ADatabaseThatCannotBeOpened_IsEplanOperationFailed()
    {
        _database.Broken = true;

        CallToolResult result = await PartTools.PartSearchAsync(_client, "3RH21", string.Empty, string.Empty, PartTools.DefaultSearchLimit, string.Empty, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains(ErrorReason.EplanOperationFailed, ((TextContentBlock)result.Content[0]).Text, StringComparison.Ordinal);
    }

    private Task<CallToolResult> List(string manufacturer = "", string text = "", string language = "", int limit = PartTools.DefaultLimit, string cursor = "") =>
        PartTools.PartListAsync(_client, string.Empty, manufacturer, text, language, limit, cursor, TestContext.Current.CancellationToken);

    private static JsonElement Structured(CallToolResult result)
    {
        ToolResultAssert.Succeeded(result);
        return result.StructuredContent!.Value;
    }

    private static List<string> Numbers(JsonElement data) => [.. data.GetProperty("parts").EnumerateArray().Select(p => p.GetProperty("partNumber").GetString()!)];

    private static JsonElement Part(JsonElement data, string partNumber) =>
        data.GetProperty("parts").EnumerateArray().Single(p => p.GetProperty("partNumber").GetString() == partNumber);

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
