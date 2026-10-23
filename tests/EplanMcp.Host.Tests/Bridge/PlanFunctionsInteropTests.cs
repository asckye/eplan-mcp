using System.Text.Json.Nodes;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Host.Tests.Bridge;

[Trait("Size", "Medium")]
public sealed class PlanFunctionsInteropTests
{
    [Fact]
    public async Task Invoke_ReadOnlyOperation_SurvivesBothSerializersAndListsMoreThanFifty()
    {
        PlanFunction[] functions = [.. Enumerable.Range(1, 60).Select(i => new PlanFunction
        {
            Type = 17, Oid = i, Name = "+A1-K1", IsMain = i <= 2, Page = i == 60 ? null : i % 2 == 0 ? "+A1/2" : "+A1/1",
        })];
        await using var fake = new PlanFunctionsBridge(functions);
        PlanFunctionsData data = await fake.Client.InvokeAsync<PlanFunctionsArgs, PlanFunctionsData>(OperationNames.PlanFunctions,
            new() { Project = PlanFunctionsBridge.Project, Names = ["+A1-K1"] }, TestContext.Current.CancellationToken);
        Assert.True(data.Complete);
        Assert.Equal(60, data.Functions.Count);
        Assert.Null(data.Functions[0].Page);
        Assert.Equal(2, data.Functions.Count(f => f.IsMain));
        Assert.Equal(10_000, data.Limit);
        Assert.Equal(PlanFunctionsBridge.Project, data.ProjectPath);
        BridgeStatus status = await fake.Client.GetStatusAsync(TestContext.Current.CancellationToken);
        Assert.Contains(OperationNames.PlanFunctions, status.Handshake!.Operations!);
    }

    [Fact]
    public async Task Invoke_OverLimit_CarriesIncompleteFlagInsteadOfClaimingACompleteSet()
    {
        await using var fake = new PlanFunctionsBridge([.. Enumerable.Range(1, 10_001)
            .Select(i => new PlanFunction { Type = 17, Oid = i, Name = "+A1-K1" })]);
        PlanFunctionsData data = await fake.Client.InvokeAsync<PlanFunctionsArgs, PlanFunctionsData>(OperationNames.PlanFunctions,
            new() { Project = PlanFunctionsBridge.Project, Names = ["+A1-K1"] }, TestContext.Current.CancellationToken);
        Assert.False(data.Complete);
        Assert.Equal(10_000, data.Functions.Count);
    }
}

/// <summary>Real Host client and Bridge operation through both serializers and a scripted channel; never discovers EPLAN.</summary>
internal sealed class PlanFunctionsBridge : IAsyncDisposable
{
    public const string Project = @"D:\Projects\Line1";
    public BridgeClient Client { get; }
    public List<PlanFunctionsArgs> Calls { get; } = [];

    public PlanFunctionsBridge(IReadOnlyList<PlanFunction> functions)
    {
        var operation = new PlanFunctionsOperation(new Catalog(), new Reader(functions));
        var endpoint = new BridgeEndpoint("EplanMcpTest.PlanFunctions", 123);
        var discovery = new FakeDiscovery();
        discovery.Endpoints.Add(endpoint);
        var connector = new FakeConnector(e => new FakeChannel(e, (method, envelope, _) =>
        {
            Assert.Equal(BridgeProtocol.InvokeMethod, method);
            Assert.Equal(OperationNames.PlanFunctions, envelope!["operation"]!.GetValue<string>());
            var args = (PlanFunctionsArgs)operation.ParseArgs(JToken.Parse(envelope["args"]!.ToJsonString()));
            Calls.Add(args);
            try
            {
                object? data = operation.Execute(args);
                return Task.FromResult(JsonNode.Parse(new JObject { ["data"] = JToken.FromObject(data!) }.ToString()));
            }
            catch (BridgeErrorException ex) { throw FakeChannel.BusinessError(ex.Error.Reason, ex.Error.Message); }
        })
        {
            Handshake = (_, _, _) => Task.FromResult(FakeChannel.CompatibleHandshake(e.ProcessId, operations: [OperationNames.PlanFunctions])),
        });
        Client = new(discovery, connector, NullLogger<BridgeClient>.Instance);
    }

    public ValueTask DisposeAsync() => Client.DisposeAsync();

    private sealed class Catalog : IProjectCatalog
    {
        public IReadOnlyList<OpenProject> ListOpenProjects() => [new() { Name = "Line1", Path = Project, ReadOnly = true }];
    }

    private sealed class Reader(IReadOnlyList<PlanFunction> functions) : IPlanFunctionReader
    {
        public IReadOnlyList<PlanFunction> Read(string projectPath) => functions;
    }

}
