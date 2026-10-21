using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using EplanMcp.Workbench.Protocol;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>
/// A Workbench pipe server in the test process, on a pipe of the test's own. It answers the handshake with
/// <see cref="Welcome"/> (or <see cref="HandshakeReply"/>: null keeps silent), hands each question to
/// <see cref="Answer"/>, sends the frames it returns (none: it goes on reading; null: it hangs up), and records
/// notifications until the Host hangs up. Two pipe instances listen from before the constructor returns, and a new one
/// starts as each connection arrives, up to the given number of instances: the channel's 250 ms connect must never
/// find every instance taken on a busy test machine, where the next instance may start late. Its lists are filled on
/// the server's threads.
/// </summary>
internal sealed class FakeWorkbench : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _connected = new(0);
    private readonly List<Task> _serving = [];
    private readonly int _instances;
    private int _connections;

    public FakeWorkbench(string pipeName, int instances = 4)
    {
        PipeName = pipeName;
        _instances = instances;
        for (int i = 0; i < Math.Min(2, instances); i++)
        {
            Listen();
        }
    }

    public string PipeName { get; }

    public WorkbenchHandshakeResult Welcome { get; set; } = new(WorkbenchProtocol.CurrentVersion, "test", Accepting: true);

    public Func<long, byte[]?>? HandshakeReply { get; set; }

    /// <summary>Whether to hang up right after answering a question, as a Workbench that quits.</summary>
    public bool HangUpAfterAnswer { get; set; }

    /// <summary>The frames to answer a question with, given the question, its JSON-RPC ID and the server's stop; null hangs up.</summary>
    public Func<ApprovalAskParams, long, CancellationToken, Task<IReadOnlyList<byte[]>?>> Answer { get; set; } = Decide(WorkbenchDecision.Approved);

    public int Connections => Volatile.Read(ref _connections);

    public ConcurrentQueue<WorkbenchHandshake> Handshakes { get; } = new();

    public ConcurrentQueue<ApprovalAskParams> Asks { get; } = new();

    public ConcurrentQueue<(string Method, JsonElement? Params)> Notifications { get; } = new();

    /// <summary>Set when a handshake arrives.</summary>
    public TaskCompletionSource Greeted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Set when a question arrives, before it is answered.</summary>
    public TaskCompletionSource<ApprovalAskParams> Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Set when a connection ends.</summary>
    public TaskCompletionSource HungUp { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Answers with the question's own binding and <paramref name="decision"/>.</summary>
    public static Func<ApprovalAskParams, long, CancellationToken, Task<IReadOnlyList<byte[]>?>> Decide(WorkbenchDecision decision) =>
        (ask, id, _) => Task.FromResult<IReadOnlyList<byte[]>?>([WorkbenchJson.Result(id, new ApprovalAskResult(ask.RequestId, ask.Fingerprint, ask.ArgumentsHash, decision))]);

    /// <summary>Waits until <paramref name="count"/> connections were made, counting from the start.</summary>
    public async Task ConnectedAsync(int count, CancellationToken cancellationToken)
    {
        // Bounded, so a connection that never came fails the test instead of hanging the run.
        for (int i = 0; i < count; i++)
        {
            if (!await _connected.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
            {
                throw new TimeoutException($"Connection {i + 1} of {count} did not reach the fake Workbench within 30 s.");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        while (true)
        {
            Task[] serving;
            lock (_serving)
            {
                serving = [.. _serving];
            }

            await Task.WhenAll(serving);
            lock (_serving)
            {
                if (_serving.Count == serving.Length)
                {
                    break;
                }
            }
        }

        _stop.Dispose();
        _connected.Dispose();
    }

    /// <summary>Starts the next instance, which exists when this returns (ServeAsync creates it before its first await).</summary>
    private void Listen()
    {
        lock (_serving)
        {
            _serving.Add(ServeAsync());
        }
    }

    /// <summary>A new instance; null when all are taken, as a test of a busy Workbench wants.</summary>
    private NamedPipeServerStream? Create()
    {
        try
        {
            return new NamedPipeServerStream(PipeName, PipeDirection.InOut, _instances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private async Task ServeAsync()
    {
        await using NamedPipeServerStream? pipe = Create();
        if (pipe is null)
        {
            return;
        }

        try
        {
            await pipe.WaitForConnectionAsync(_stop.Token);
            Listen();
            Interlocked.Increment(ref _connections);
            _connected.Release();
            await ConnectionAsync(pipe, _stop.Token);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            return;
        }
        catch (IOException)
        {
            // The Host hung up, or sent what the protocol refuses.
        }

        HungUp.TrySetResult();
    }

    private async Task ConnectionAsync(NamedPipeServerStream pipe, CancellationToken stop)
    {
        while (await WorkbenchFraming.ReadAsync(pipe, stop) is { } frame)
        {
            RpcMessage message = WorkbenchJson.Read(frame);
            switch (message.Method)
            {
                case WorkbenchProtocol.HandshakeMethod:
                    Handshakes.Enqueue(WorkbenchJson.As<WorkbenchHandshake>(message.Params));
                    Greeted.TrySetResult();
                    if ((HandshakeReply is null ? WorkbenchJson.Result(message.Id!.Value, Welcome) : HandshakeReply(message.Id!.Value)) is { } reply)
                    {
                        await WorkbenchFraming.WriteAsync(pipe, reply, stop);
                    }

                    break;
                case WorkbenchProtocol.ApprovalAskMethod:
                    var ask = WorkbenchJson.As<ApprovalAskParams>(message.Params);
                    Asks.Enqueue(ask);
                    Asked.TrySetResult(ask);
                    if (await Answer(ask, message.Id!.Value, stop) is not { } frames)
                    {
                        return;
                    }

                    foreach (byte[] answer in frames)
                    {
                        await WorkbenchFraming.WriteAsync(pipe, answer, stop);
                    }

                    if (HangUpAfterAnswer)
                    {
                        return;
                    }

                    break;
                default:
                    Notifications.Enqueue((message.Method!, message.Params));
                    break;
            }
        }
    }
}
