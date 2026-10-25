using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Host.Tests.Bridge;

[Trait("Size", "Medium")]
public sealed class PlanStepGuardInteropTests : IAsyncDisposable
{
    private readonly PlanGuardReader _reader = new();
    private readonly Dispatcher _dispatcher = new();
    private readonly BridgeClient _client;
    private readonly PlanStep _step;
    private readonly string _resource = new('a', 64);
    private readonly PlanBinding _binding = new(3, new string('b', 64));

    public PlanStepGuardInteropTests()
    {
        var operation = new PlanStepGuardOperation(_reader, _reader, _reader, _reader, () => _reader.Milliseconds);
        var router = new OperationRouter([operation], _dispatcher, null, new Log(), TimeSpan.FromSeconds(10));
        var endpoint = new BridgeEndpoint("EplanMcpTest.Guard", 123);
        var discovery = new FakeDiscovery();
        discovery.Endpoints.Add(endpoint);
        var connector = new FakeConnector(e => new FakeChannel(e, (method, envelope, _) =>
        {
            Assert.Equal(BridgeProtocol.InvokeMethod, method);
            Assert.Equal(OperationNames.PlanStepGuard, envelope!["operation"]!.GetValue<string>());
            try
            {
                JToken data = router.Invoke(OperationNames.PlanStepGuard, JToken.Parse(envelope["args"]!.ToJsonString()), "guard");
                return Task.FromResult(JsonNode.Parse(new JObject { ["data"] = data }.ToString()));
            }
            catch (BridgeErrorException ex)
            {
                throw new BridgeRpcException(BridgeProtocol.BusinessErrorCode, ex.Error.Message, JsonNode.Parse(BridgeJson.FromObject(ex.Error).ToString()));
            }
        })
        {
            Handshake = (_, _, _) => Task.FromResult(FakeChannel.CompatibleHandshake(e.ProcessId, operations: [OperationNames.PlanStepGuard])),
        });
        _client = new(discovery, connector, NullLogger<BridgeClient>.Instance);
        PlanScope scope = PlanScopeRules.Pages(["+A1/1"]);
        SnapshotObject[] objects = _reader.Objects();
        var checks = new List<PlanPrecondition>
        {
            new PlanMembers(scope, PlanSnapshotHashes.Members(_reader.Path, objects.Select(o =>
                new KeyValuePair<string, string>(PlanSnapshotHashes.Key(o.Kind, o.Type, o.Oid), o.Hash))), objects.Length),
            new PlanPageHash(PlanScopeRules.Locator(_reader.Path, objects[0]), SnapshotCanonical.PageHash("standard", objects)),
            new PlanAbsent("page", "+A1/9"),
            new PlanAbsent("function", "+A1-K9"),
            new PlanResourceHash(_resource),
            _binding,
        };
        checks.AddRange(objects.Select(o => new PlanObjectHash(PlanScopeRules.Locator(_reader.Path, o), o.Hash)));
        _step = new(0, "page.update", JsonSerializer.SerializeToElement(new { }), ToolTier.Write, scope, scope,
            [.. checks], [], [], [], null, true);
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        _reader.Dispose();
    }

    [Fact]
    public async Task TypedClient_AllConditionsMatch_RoundTripsBothSerializersAndOneDispatch()
    {
        Assert.Equal("ok", await CheckAsync());
        Assert.Equal((1, 1, 1, 1), (_dispatcher.Calls, _reader.Opens, _reader.Reads, _reader.Disposals));
        BridgeStatus status = await _client.GetStatusAsync(TestContext.Current.CancellationToken);
        Assert.Contains(OperationNames.PlanStepGuard, status.Handshake!.Operations!);
    }

    [Theory]
    [InlineData("hash", "members,objectHash,pageHash")]
    [InlineData("added", "members,pageHash")]
    [InlineData("removed", "members,not_found,pageHash")]
    [InlineData("pageName", "absent")]
    [InlineData("functionName", "absent")]
    [InlineData("resource", "resource")]
    [InlineData("binding", "binding")]
    public async Task TypedClient_ChangeBetweenPlanAndGuard_ReturnsFullStructuredConflictString(string change, string reasons)
    {
        Assert.Equal("ok", await CheckAsync());
        switch (change)
        {
            case "hash":
                _reader.Content[1][0].Values = [new(11011, null, "en_US@Changed;")];
                break;
            case "added":
                _reader.Content[1].Add(new() { Kind = "function", Identifier = "28/17/3/0", Oid = 3, Name = "+A1-K2", Page = "+A1/1" });
                break;
            case "removed":
                _reader.Content[1].RemoveAt(1);
                break;
            case "pageName":
                _reader.Content[9] = [new() { Kind = "page", Identifier = "28/4/9/0", Oid = 9, Name = "+A1/9" }];
                break;
            case "functionName":
                _reader.Names.Add(new() { Type = 17, Oid = 9, Name = "+A1-K9:1" });
                break;
        }
        BridgeCallException failure = await Assert.ThrowsAsync<BridgeCallException>(() => CheckAsync(
            change == "resource" ? new string('c', 64) : null,
            change == "binding" ? _binding with { Revision = 4 } : null));
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (failure.Error.Reason, failure.Error.Execution));
        Assert.Equal(["conflicts"], failure.Error.Metadata!.Keys);
        string text = failure.Error.Metadata["conflicts"];
        using JsonDocument json = JsonDocument.Parse(text);
        Assert.Equal(text, PlanCanonical.Serialize(json.RootElement));
        Assert.Equal(reasons.Split(','), json.RootElement.EnumerateArray().Select(c => c.GetProperty("reason").GetString()).Order(StringComparer.Ordinal));
        Assert.All(json.RootElement.EnumerateArray(), c =>
        {
            Assert.True(c.TryGetProperty("locator", out _) ^ c.TryGetProperty("scope", out _));
            Assert.True(c.TryGetProperty("expected", out _));
            Assert.True(c.TryGetProperty("actual", out _));
        });
        Assert.Equal(2, _dispatcher.Calls);
    }

    [Fact]
    public async Task TypedClient_Timeout_IsNotStartedAndNeverReplays()
    {
        _reader.PerRead = 10_000;
        BridgeCallException failure = await Assert.ThrowsAsync<BridgeCallException>(() => CheckAsync());
        Assert.Equal((ErrorReason.Timeout, ExecutionState.NotStarted), (failure.Error.Reason, failure.Error.Execution));
        Assert.Equal(1, _dispatcher.Calls);
    }

    [Fact]
    public async Task TypedClient_ResourceFileChanged_RehashesActualBytesAndRejects()
    {
        string root = Directory.CreateTempSubdirectory("eplanmcp-guard-resource-").FullName;
        try
        {
            string path = Path.Combine(root, "resource.ema");
            await File.WriteAllBytesAsync(path, [1, 2, 3], TestContext.Current.CancellationToken);
            string expected = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)));
            PlanStep step = _step with { Preconditions = [.. _step.Preconditions.Where(p => p is not PlanResourceHash), new PlanResourceHash(expected)] };
            Assert.Equal("ok", await new PlanStepGuard(_client).CheckAsync(_reader.Path, step, [expected], _binding, TestContext.Current.CancellationToken));
            await File.WriteAllBytesAsync(path, [4, 5, 6], TestContext.Current.CancellationToken);
            string actual = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)));
            BridgeCallException failure = await Assert.ThrowsAsync<BridgeCallException>(() => new PlanStepGuard(_client)
                .CheckAsync(_reader.Path, step, [actual], _binding, TestContext.Current.CancellationToken));
            using JsonDocument json = JsonDocument.Parse(failure.Error.Metadata!["conflicts"]);
            Assert.Equal("resource", Assert.Single(json.RootElement.EnumerateArray()).GetProperty("reason").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private Task<string> CheckAsync(string? resource = null, PlanBinding? binding = null) => new PlanStepGuard(_client)
        .CheckAsync(_reader.Path, _step, [resource ?? _resource], binding ?? _binding, TestContext.Current.CancellationToken);

    private sealed class Dispatcher : IMainThreadDispatcher
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

    private sealed class Log : IBridgeLog
    {
        public void Write(BridgeLogLevel level, string message, Exception? exception = null)
        {
        }
    }
}
