using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// Answers tools from fixed data, or fails every call with <see cref="Failure"/>. <c>project.list</c> answers from
/// <see cref="Projects"/>; other operations through <see cref="Handlers"/>, which receive the arguments the tool sent.
/// Every call's arguments are kept in <see cref="Calls"/>.
/// </summary>
internal sealed class FakeBridgeClient : IBridgeClient
{
    public List<OpenProject> Projects { get; } = [];

    public Dictionary<string, Func<object?, object>> Handlers { get; } = [];

    public List<(string Operation, object? Args)> Calls { get; } = [];

    public BridgeError? Failure { get; set; }

    public BridgeStatus Status { get; set; } = new([], null, null);

    public int Invocations { get; private set; }

    /// <summary>The arguments of the last call, as the tool built them.</summary>
    public object? LastArgs => Calls.Count == 0 ? null : Calls[^1].Args;

    public Task<BridgeStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(Status);

    public Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, CancellationToken cancellationToken)
    {
        Invocations++;
        Calls.Add((operation, args));
        if (Failure is not null)
        {
            throw new BridgeCallException(Failure);
        }

        object data = operation == OperationNames.ProjectList
            ? new ProjectListData { Projects = [.. Projects] }
            : Handlers.TryGetValue(operation, out Func<object?, object>? handler)
                ? handler(args)
                : throw new InvalidOperationException("unexpected operation " + operation);
        return Task.FromResult((TData)data);
    }
}
