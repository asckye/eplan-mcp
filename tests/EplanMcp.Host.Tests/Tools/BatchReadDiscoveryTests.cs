using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;
using EplanMcp.Large.Tests;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class BatchReadDiscoveryTests
{
    private const string Project = "X:/fixture/Line1";
    private const string Page = "=A/1";
    private const string NameService = "O:Eplan.EplApi.HEServices.NameService.CanDeviceTagBeNested";
    private static readonly BatchEntry[][] _readBatches = [.. BatchValues.ApiEntries().Where(BatchReadRecipes.Default.CanBuild).Chunk(8)];

    private static BatchEntry Entry(string group) => Assert.Single(BatchValues.ApiEntries(), e => e.Group == group && e.Kind == "api-read");

    private static FakeBridgeClient Bridge()
    {
        FakeBridgeClient bridge = ObjectReadToolTests.Bridge(1);
        bridge.Handlers[OperationNames.PageList] = value =>
        {
            var args = Assert.IsType<PageListArgs>(value);
            Assert.Equal(Project, args.Project);
            Assert.Equal("Circuit", args.PageType);
            Assert.Equal(1, args.Limit);
            return new ListData<PageSummary> { Project = Project, Items = [new() { Name = Page, Type = "Circuit" }], Total = 1 };
        };
        return bridge;
    }

    public static TheoryData<int> ReadRecipeBatches()
    {
        var batches = new TheoryData<int>();
        for (int index = 0; index < _readBatches.Length; index++)
        { batches.Add(index); }
        return batches;
    }

    [Theory]
    [MemberData(nameof(ReadRecipeBatches))]
    public async Task CatalogReadRecipes_ProduceCompleteCallsThroughTheOfflineFixture(int batch)
    {
        Assert.NotEmpty(_readBatches);
        foreach (BatchEntry entry in _readBatches[batch])
        {
            FakeBridgeClient bridge = Bridge();
            BatchArguments result = await new BatchReadDiscovery(bridge, Project).ArgumentsAsync(entry, TestContext.Current.CancellationToken);

            Assert.True(result.Outcome is null, entry.Id + ": " + result.Detail);
            Assert.NotNull(result.Values);
            Assert.True(ApiTryTools.MethodParameters(entry.Group, JsonSerializer.SerializeToElement(result.Values)), entry.Id);
            Assert.Equal(entry.ObjectKinds is not null, result.Values.ContainsKey("target"));
            Assert.All(bridge.Calls, call => Assert.Contains(call.Operation,
                new[] { OperationNames.ProjectGet, OperationNames.ObjectFind, OperationNames.PageList }));
        }
    }

    [Fact]
    public async Task NameService_UsesExplicitCircuitPageAndFunctionOnThatPage()
    {
        FakeBridgeClient bridge = Bridge();
        BatchEntry entry = Entry(NameService);
        Assert.NotNull(entry.Parameters);
        string pageParameter = Assert.Single(entry.Parameters, p => p.Value[0] == "Eplan.EplApi.DataModel.Page").Key;
        string functionParameter = Assert.Single(entry.Parameters, p => p.Value[0] == "Eplan.EplApi.DataModel.Function").Key;
        BatchArguments result = await new BatchReadDiscovery(bridge, Project).ArgumentsAsync(entry, TestContext.Current.CancellationToken);

        Assert.Null(result.Outcome);
        Assert.NotNull(result.Values);
        Assert.True(ApiTryTools.MethodParameters(entry.Group, JsonSerializer.SerializeToElement(result.Values)));
        Assert.Equal(Page, Assert.IsType<JsonElement>(result.Values[pageParameter]).GetProperty("page").GetString());
        Assert.Equal(Page, Assert.IsType<JsonElement>(result.Values[functionParameter]).GetProperty("page").GetString());
        ObjectReadArgs[] finds = [.. bridge.Calls.Where(c => c.Operation == OperationNames.ObjectFind).Select(c => Assert.IsType<ObjectReadArgs>(c.Args))];
        Assert.Contains(finds, a => a.ObjectType == "Page" && a.Page == Page);
        Assert.Contains(finds, a => ApiTryCatalog.Kinds("Eplan.EplApi.DataModel.Function").Contains(a.ObjectType, StringComparer.OrdinalIgnoreCase) && a.Page == Page);
        Assert.Contains(finds, a => a.ObjectType == "Project" && a.Page is null);
    }

    [Fact]
    public async Task MissingCircuitPage_IsNoContextValueAndDoesNotSearchArguments()
    {
        FakeBridgeClient bridge = Bridge();
        bridge.Handlers[OperationNames.PageList] = _ => new ListData<PageSummary>();
        BatchArguments result = await new BatchReadDiscovery(bridge, Project).ArgumentsAsync(Entry(NameService), TestContext.Current.CancellationToken);

        Assert.Null(result.Values);
        Assert.Equal("skipped:no_context_value", result.Outcome);
        Assert.DoesNotContain(bridge.Calls, c => c.Operation == OperationNames.ObjectFind);
    }

    [Fact]
    public async Task MissingParameterObject_IsNoContextValue()
    {
        FakeBridgeClient bridge = Bridge();
        Func<object?, object> find = bridge.Handlers[OperationNames.ObjectFind];
        bridge.Handlers[OperationNames.ObjectFind] = args => Assert.IsType<ObjectReadArgs>(args).ObjectType is "Project" or "Page" ? find(args) : new ObjectReadData();
        BatchArguments result = await new BatchReadDiscovery(bridge, Project).ArgumentsAsync(Entry(NameService), TestContext.Current.CancellationToken);

        Assert.Null(result.Values);
        Assert.Equal("skipped:no_context_value", result.Outcome);
        Assert.Contains("Function", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingReceiver_IsNoObject()
    {
        FakeBridgeClient bridge = Bridge();
        bridge.Handlers[OperationNames.ObjectFind] = _ => new ObjectReadData();
        BatchArguments result = await new BatchReadDiscovery(bridge, Project).ArgumentsAsync(Entry("O:Eplan.EplApi.DataModel.Page.Name"), TestContext.Current.CancellationToken);

        Assert.Null(result.Values);
        Assert.Equal("skipped:no_object", result.Outcome);
    }

    [Fact]
    public async Task UnknownParameterType_StillNeedsContextWithoutCallingBridge()
    {
        var bridge = new FakeBridgeClient();
        var entry = new BatchEntry("unknown", "api-read", ["Project"], Parameters: new() { ["unknown"] = ["Unknown.Type"] });
        BatchArguments result = await new BatchReadDiscovery(bridge, Project).ArgumentsAsync(entry, TestContext.Current.CancellationToken);

        Assert.Null(result.Values);
        Assert.Equal("failed:needs_context", result.Outcome);
        Assert.Empty(bridge.Calls);
    }

    [Theory]
    [InlineData(ErrorReason.EplanOperationFailed)]
    [InlineData(ErrorReason.Timeout)]
    public async Task BridgeFailure_PreservesErrorInsteadOfReportingMissingContext(string reason)
    {
        FakeBridgeClient bridge = Bridge();
        var error = new BridgeError { Reason = reason, Message = "Read failed." };
        bridge.Handlers[OperationNames.ObjectFind] = _ => throw new BridgeCallException(error);
        BridgeCallException exception = await Assert.ThrowsAsync<BridgeCallException>(async () =>
            await new BatchReadDiscovery(bridge, Project).ArgumentsAsync(Entry(NameService), TestContext.Current.CancellationToken));

        Assert.Equal(reason, exception.Error.Reason);
        Assert.Equal(error.Message, exception.Error.Message);
    }
}
