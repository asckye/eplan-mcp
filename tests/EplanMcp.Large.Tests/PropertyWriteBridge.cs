using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

/// <summary>Calls the real add-in's B1 operations through the Host client, before a property.set Host tool exists.</summary>
internal sealed class PropertyWriteBridge(BridgeClient client) : IAsyncDisposable
{
    public BridgeClient Client => client;

    public static async Task<PropertyWriteBridge> StartAsync(int processId)
    {
#pragma warning disable CA2000 // Ownership transfers to PropertyWriteBridge; setup failure disposes the client below.
        BridgeClient client = await LiveSnapshots.BridgeAsync(processId);
#pragma warning restore CA2000
        try
        {
            HandshakeResult handshake = await client.SelectAsync(processId, TestContext.Current.CancellationToken);
            Assert.Contains(OperationNames.PropertySetPlan, handshake.Operations!);
            Assert.Contains(OperationNames.PropertySetExecute, handshake.Operations!);
            return new PropertyWriteBridge(client);
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    public async Task<PropertySetArgs> PlanAsync(string project, List<PropertySetChange> changes)
    {
        var args = new PropertySetArgs { Project = project, Changes = changes };
        args.Approved = await client.InvokeAsync<PropertySetArgs, PropertySetPlan>(OperationNames.PropertySetPlan, args,
            TimeSpan.FromSeconds(35), null, TestContext.Current.CancellationToken);
        return args;
    }

    public async Task<PropertySetData> ExecuteAsync(PropertySetArgs args) =>
        (await client.InvokeWriteAsync<PropertySetArgs, PropertySetData>(OperationNames.PropertySetExecute,
            args, Guid.NewGuid().ToString("N"), TimeSpan.FromSeconds(35), null, TestContext.Current.CancellationToken)).Data!;

    public ValueTask DisposeAsync() => client.DisposeAsync();
}
