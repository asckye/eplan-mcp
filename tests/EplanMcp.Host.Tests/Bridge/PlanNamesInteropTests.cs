using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Host.Tests.Bridge;

/// <summary>Real Host client and router through both serializers; a scripted channel never discovers EPLAN.</summary>
[Trait("Size", "Medium")]
public sealed class PlanNamesInteropTests : IAsyncDisposable
{
    private const string Project = @"D:\Projects\Line1";
    private readonly Reader _reader = new();
    private readonly InlineDispatcher _dispatcher = new();
    private readonly BridgeClient _client;

    public PlanNamesInteropTests()
    {
        var router = new OperationRouter([new PlanNamesOperation(new Catalog(), _reader, () => 0)], _dispatcher,
            null, new NullLog(), TimeSpan.FromSeconds(5));
        var discovery = new FakeDiscovery();
        discovery.Endpoints.Add(new("EplanMcpTest.PlanNames", 123));
        var connector = new FakeConnector(endpoint => new FakeChannel(endpoint, (method, envelope, _) =>
        {
            Assert.Equal(BridgeProtocol.InvokeMethod, method);
            Assert.Equal(OperationNames.PlanNames, envelope!["operation"]!.GetValue<string>());
            Assert.Equal(["project"], envelope["args"]!.AsObject().Select(p => p.Key));
            try
            {
                JToken data = router.Invoke(OperationNames.PlanNames, JToken.Parse(envelope["args"]!.ToJsonString()),
                    envelope["correlationId"]!.GetValue<string>());
                return Task.FromResult(JsonNode.Parse(new JObject { ["data"] = data }.ToString()));
            }
            catch (BridgeErrorException ex)
            {
                throw new BridgeRpcException(BridgeProtocol.BusinessErrorCode, ex.Error.Message,
                    JsonSerializer.SerializeToNode(ex.Error, HostJson.Options));
            }
        })
        {
            Handshake = (_, _, _) => Task.FromResult(FakeChannel.CompatibleHandshake(endpoint.ProcessId,
                operations: [.. router.SupportedOperations])),
        });
        _client = new(discovery, connector, NullLogger<BridgeClient>.Instance);
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();

    [Fact]
    public async Task Invoke_ReadOnlyNames_OneDispatchAndEnumerationWithHandshakeAndCompilerDigest()
    {
        _reader.Inventory.Pages.AddRange(["+B/2", "+A/1", "+A/1"]);
        foreach (PlanFunction function in new PlanFunction[]
        {
            new() { Type = 17, Oid = 1, Name = "=F+A-K1", IsMain = true, Page = "+A/1" },
            new() { Type = 17, Oid = 2, Name = "=F+A-K1", Page = "+B/2" },
            new() { Type = 17, Oid = 3, Name = "=F+A-电机😀", Page = null },
            new() { Type = 17, Oid = 4, Name = "" },
        })
        {
            _reader.Inventory.AddFunction(function);
        }

        PlanNamesData data = await new PlanNamesReader(_client).ReadAsync("Line1", TestContext.Current.CancellationToken);
        Assert.Equal(("Line1", Project, "sample@123", true), (data.Project, data.ProjectPath, data.ProjectGuid, data.Complete));
        Assert.Equal([("function", "=F+A-K1"), ("function", "=F+A-电机😀"), ("page", "+A/1"), ("page", "+B/2")],
            data.Names.Select(n => (n.Kind, n.Name)));
        Assert.Equal(PlanSnapshotHashes.Names(Project, data.Names.Select(n => new PlanOccupiedName(n.Kind, n.Name))), data.NamesHash);
        Assert.Equal((1, 1, Project), (_dispatcher.Calls, _reader.Calls, _reader.Path));
        BridgeStatus status = await _client.GetStatusAsync(TestContext.Current.CancellationToken);
        Assert.Equal(BridgeProtocol.CurrentVersion, status.Handshake!.ProtocolVersion);
        Assert.Contains(OperationNames.PlanNames, status.Handshake.Operations!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invoke_RealLimits_ReturnsIncompleteAndTypedHostCallRefuses(bool bytes)
    {
        if (bytes)
        {
            _reader.Inventory.Pages.Add(new string('电', PlanNamesOperation.MaxJsonBytes / 3 + 1));
        }
        else
        {
            _reader.Inventory.Pages.AddRange(Enumerable.Range(0, PlanNamesOperation.MaxNames + 1)
                .Select(i => i.ToString("X", CultureInfo.InvariantCulture)));
        }

        PlanNamesData data = await _client.InvokeAsync<PlanNamesArgs, PlanNamesData>(OperationNames.PlanNames,
            new() { Project = Project }, TestContext.Current.CancellationToken);
        Assert.False(data.Complete);
        Assert.Empty(data.Names);
        Assert.Null(data.NamesHash);
        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() =>
            new PlanNamesReader(_client).ReadAsync(Project, TestContext.Current.CancellationToken));
        Assert.Equal(ErrorReason.CapabilityMissing, error.Error.Reason);
        Assert.Equal("plan.names:complete", error.Error.Metadata!["missing"]);
        Assert.Null(error.Error.Execution);
    }

    [Fact]
    public async Task Invoke_MissingHandshakeCapability_RefusesBeforeInvoke()
    {
        var discovery = new FakeDiscovery();
        discovery.Endpoints.Add(new("EplanMcpTest.MissingNames", 123));
        int invokes = 0;
        var connector = new FakeConnector(endpoint => new FakeChannel(endpoint, (_, _, _) =>
        {
            invokes++;
            throw new InvalidOperationException("No invoke is allowed without the capability.");
        }));
        await using var client = new BridgeClient(discovery, connector, NullLogger<BridgeClient>.Instance);
        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() =>
            new PlanNamesReader(client).ReadAsync(Project, TestContext.Current.CancellationToken));
        Assert.Equal(ErrorReason.CapabilityMissing, error.Error.Reason);
        Assert.Equal(0, invokes);
    }

    private sealed class Reader : IPlanNameReader
    {
        public PlanNameInventory Inventory { get; } = new() { ProjectGuid = "sample@123" };
        public int Calls { get; private set; }
        public string? Path { get; private set; }
        public IReadOnlyList<PlanFunction> Read(string projectPath) => throw new InvalidOperationException("No second enumeration.");

        public PlanNameInventory ReadNames(string projectPath, Action checkpoint)
        {
            Calls++;
            Path = projectPath;
            checkpoint();
            return Inventory;
        }
    }

    private sealed class Catalog : IProjectCatalog
    {
        public IReadOnlyList<OpenProject> ListOpenProjects() => [new() { Name = "Line1", Path = Project, ReadOnly = true }];
    }

    private sealed class InlineDispatcher : IMainThreadDispatcher
    {
        public int Calls { get; private set; }

        public DispatchResult Run(Func<object?> work, TimeSpan startTimeout)
        {
            Calls++;
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
