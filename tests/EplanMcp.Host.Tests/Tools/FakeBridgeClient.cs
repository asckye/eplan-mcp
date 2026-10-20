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

    /// <summary>The process IDs passed to <see cref="SelectAsync"/>, which answers with <see cref="Status"/>'s handshake.</summary>
    public List<int> Selections { get; } = [];

    public Task<BridgeStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(Status);

    public Task<HandshakeResult> SelectAsync(int processId, CancellationToken cancellationToken)
    {
        Selections.Add(processId);
        return Failure is not null
            ? throw new BridgeCallException(Failure)
            : Task.FromResult(Status.Handshake ?? throw new InvalidOperationException("no handshake to answer select with"));
    }

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

    /// <summary>The timeouts given to the long-waiting overload, in order.</summary>
    public List<TimeSpan> Timeouts { get; } = [];

    public Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, TimeSpan timeout, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken)
    {
        Timeouts.Add(timeout);
        return InvokeAsync<TArgs, TData>(operation, args, cancellationToken);
    }

    /// <summary>The request IDs and limits of writes, in order.</summary>
    public List<(string RequestId, TimeSpan Limit)> Writes { get; } = [];

    /// <summary>What <see cref="FetchOutcomeAsync"/> answers, by request ID; missing means the Bridge cannot be asked.</summary>
    public Dictionary<string, OutcomeGetData> Outcomes { get; } = new(StringComparer.Ordinal);

    public Task<OutcomeGetData?> FetchOutcomeAsync(int processId, string requestId, CancellationToken cancellationToken) =>
        Task.FromResult(Outcomes.GetValueOrDefault(requestId));

    /// <summary>When set, writes fail with it (reads still answer); its <c>Execution</c> is kept as given.</summary>
    public BridgeError? WriteFailure { get; set; }

    /// <summary>When true, writes come back as committed but recovered after an interruption, without data.</summary>
    public bool WriteRecovered { get; set; }

    /// <summary>Writes answer like reads (<see cref="Handlers"/>, <see cref="Failure"/>) unless told otherwise above.</summary>
    public async Task<WriteResult<TData>> InvokeWriteAsync<TArgs, TData>(string operation, TArgs args, string requestId, TimeSpan limit, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken)
    {
        Writes.Add((requestId, limit));
        if (WriteFailure is not null)
        {
            Calls.Add((operation, args));
            throw new BridgeCallException(WriteFailure);
        }

        if (WriteRecovered)
        {
            Calls.Add((operation, args));
            return new WriteResult<TData>(default, Recovered: true);
        }

        return new WriteResult<TData>(await InvokeAsync<TArgs, TData>(operation, args, cancellationToken), Recovered: false);
    }
}
