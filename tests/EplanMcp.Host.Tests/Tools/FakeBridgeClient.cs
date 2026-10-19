using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// Answers tools from fixed data, or fails every call with <see cref="Failure"/>. <c>project.list</c> answers from
/// <see cref="Projects"/>; other operations from <see cref="Answers"/>, which receives the arguments the tool sent.
/// </summary>
internal sealed class FakeBridgeClient : IBridgeClient
{
    public List<OpenProject> Projects { get; } = [];

    public Dictionary<string, Func<object?, object>> Answers { get; } = new(StringComparer.Ordinal);

    public BridgeError? Failure { get; set; }

    public BridgeStatus Status { get; set; } = new([], null, null);

    public int Invocations { get; private set; }

    /// <summary>The arguments of the last call, as the tool built them.</summary>
    public object? LastArgs { get; private set; }

    public Task<BridgeStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(Status);

    public Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, CancellationToken cancellationToken)
    {
        Invocations++;
        LastArgs = args;
        if (Failure is not null)
        {
            throw new BridgeCallException(Failure);
        }

        object data = operation == OperationNames.ProjectList
            ? new ProjectListData { Projects = [.. Projects] }
            : Answers.TryGetValue(operation, out Func<object?, object>? answer)
                ? answer(args)
                : throw new InvalidOperationException("unexpected operation " + operation);
        return Task.FromResult((TData)data);
    }
}
