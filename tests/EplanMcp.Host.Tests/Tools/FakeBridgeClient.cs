using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>Answers tools from fixed data, or fails every call with <see cref="Failure"/>.</summary>
internal sealed class FakeBridgeClient : IBridgeClient
{
    public List<OpenProject> Projects { get; } = [];

    public BridgeError? Failure { get; set; }

    public BridgeStatus Status { get; set; } = new([], null, null);

    public int Invocations { get; private set; }

    public Task<BridgeStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(Status);

    public Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, CancellationToken cancellationToken)
    {
        Invocations++;
        if (Failure is not null)
        {
            throw new BridgeCallException(Failure);
        }

        object data = operation == OperationNames.ProjectList
            ? new ProjectListData { Projects = [.. Projects] }
            : throw new InvalidOperationException("unexpected operation " + operation);
        return Task.FromResult((TData)data);
    }
}
