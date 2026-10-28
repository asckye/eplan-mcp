using System.IO.Pipes;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Bridge.Core.Server;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Server;

[Trait("Size", "Medium")]
public sealed class PlacementTextCreateBridgeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pipe_MixedBatch_VerifiedCommitOrMidBatchMismatchRollbackAndRecoverableOutcome(bool mismatch)
    {
        var store = new PlacementTextStore();
        if (mismatch)
        { store.AfterWrite = () => { if (store.Writes == 2) { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Forced mid-batch failure."); } }; }
        var log = new ListLog();
        var dispatcher = new InlineDispatcher();
        var router = new OperationRouter([store.Operation], dispatcher, null, log, TimeSpan.FromSeconds(1));
        using var server = new BridgeServer("EplanMcp.TextTest." + Guid.NewGuid().ToString("N"),
            name => new NamedPipeServerStream(name, PipeDirection.InOut, 2, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly),
            new BridgeServerOptions { BridgeVersion = "test", Eplan = new() { ProcessId = 1 } }, router, log);
        server.Start();
        await using var client = new NamedPipeClientStream(".", server.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(5000, TestContext.Current.CancellationToken);
        JObject handshake = await Call(client, new JObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "handshake", ["params"] = new JObject { ["protocolVersion"] = 1, ["minProtocolVersion"] = 1 } });
        Assert.Contains(OperationNames.PlacementTextCreateExecute, handshake["result"]!["operations"]!.Values<string>());
        JObject request = Invoke(2, OperationNames.PlacementTextCreateExecute, "write", BridgeJson.FromObject(PlacementTextStore.Mixed()));
        JObject reply = await Call(client, request);
        if (mismatch)
        {
            Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack),
                ((string?)reply["error"]!["data"]!["reason"], (string?)reply["error"]!["data"]!["execution"]));
            Assert.Empty(store.Texts);
            Assert.Equal(["Begin AI: eplan_placement_create", "Rollback", "Dispose"], store.Transactions.Calls);
        }
        else
        {
            PlacementTextCreateData result = reply["result"]!["data"]!.ToObject<PlacementTextCreateData>(BridgeJson.Serializer)!;
            Assert.Equal(PlacementTextStore.Args().Text.Contents, result.Items[0].Contents);
            Assert.Equal([12.5, 22.5], result.Items[0].Position);
            Assert.Equal(3.5, result.Items[0].HeightMm);
            Assert.Equal(["text", "line", "rectangle"], result.Items.Select(i => i.Kind));
            Assert.Equal(3, store.Texts.Count);
            Assert.Equal(["Begin AI: eplan_placement_create", "Commit", "CloseUndo", "Dispose"], store.Transactions.Calls);
        }
        JObject outcome = await Call(client, Invoke(3, OperationNames.OutcomeGet, "query", new JObject { ["requestId"] = "write" }));
        Assert.Equal(mismatch ? OutcomeStatus.RolledBack : OutcomeStatus.Committed, (string?)outcome["result"]!["data"]!["status"]);
        JObject duplicate = await Call(client, request);
        Assert.Equal(ErrorReason.InvalidArgument, (string?)duplicate["error"]!["data"]!["reason"]);
        Assert.Equal(mismatch ? 2 : 3, store.Writes);
        Assert.Equal(1, dispatcher.Calls);
    }

    private static JObject Invoke(int id, string operation, string request, JToken args) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["method"] = "invoke",
        ["params"] = new JObject { ["operation"] = operation, ["correlationId"] = request, ["args"] = args },
    };

    private static async Task<JObject> Call(Stream stream, JObject request)
    {
        await Framing.WriteFrameAsync(stream, BridgeJson.ToUtf8(request), TestContext.Current.CancellationToken);
        return (JObject)BridgeJson.Parse((await Framing.ReadFrameAsync(stream, 1 << 20, TestContext.Current.CancellationToken))!);
    }
}
