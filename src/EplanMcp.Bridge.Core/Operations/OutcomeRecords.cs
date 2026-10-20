using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// What the Bridge knows about each write, by request ID (ADR-0010 section 2, M3 design 4.2): queued, running, then a
/// final state. Shared by every connection, because several Hosts may use one Bridge. Metadata only, in memory, at
/// most <see cref="Capacity"/> entries; queued and running ones are never evicted, and evicted IDs are remembered for a
/// while to answer "expired" instead of "not started". Thread-safe.
/// </summary>
public sealed class OutcomeRecords
{
    /// <summary>The bound decided in the M3 design (Q3).</summary>
    public const int Capacity = 256;

    // Long enough that a Host asking about an evicted write gets "expired", not "never seen"; IDs only.
    private const int EvictedMemory = 4096;

    private readonly object _gate = new();
    private readonly Dictionary<string, OutcomeGetData> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _order = new();
    private readonly HashSet<string> _evicted = new(StringComparer.Ordinal);
    private readonly Queue<string> _evictedOrder = new();

    /// <summary>
    /// Registers a write about to be dispatched as queued. False, with what is known about it, when the request ID was
    /// seen before: then the write must not run (ADR-0010: the same request ID never executes twice).
    /// </summary>
    public bool TryBegin(string requestId, string operation, out OutcomeGetData? previous)
    {
        lock (_gate)
        {
            previous = Known(requestId);
            if (previous is not null)
            {
                return false;
            }

            Add(requestId, new OutcomeGetData { Status = OutcomeStatus.Queued, Operation = operation });
            return true;
        }
    }

    /// <summary>Records the write's progress or final state; <paramref name="reason"/> is the error reason, if any.</summary>
    public void Update(string requestId, string status, string? reason = null)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(requestId, out OutcomeGetData? entry))
            {
                entry.Status = status;
                entry.Reason = reason;
            }

            Trim();
        }
    }

    /// <summary>
    /// The answer to <c>outcome.get</c>. An ID never seen is registered as not started, so a request that arrives late
    /// with it is refused and "not started" stays true.
    /// </summary>
    public OutcomeGetData Get(string requestId)
    {
        lock (_gate)
        {
            if (Known(requestId) is { } known)
            {
                return known;
            }

            var unseen = new OutcomeGetData { Status = OutcomeStatus.NotStarted };
            Add(requestId, unseen);
            return Copy(unseen);
        }
    }

    private OutcomeGetData? Known(string requestId) =>
        _entries.TryGetValue(requestId, out OutcomeGetData? entry) ? Copy(entry)
        : _evicted.Contains(requestId) ? new OutcomeGetData { Status = OutcomeStatus.Expired }
        : null;

    private void Add(string requestId, OutcomeGetData entry)
    {
        _entries[requestId] = entry;
        _order.AddLast(requestId);
        Trim();
    }

    /// <summary>Evicts the oldest final entries beyond the capacity; an entry still queued or running stays.</summary>
    private void Trim()
    {
        LinkedListNode<string>? node = _order.First;
        while (_entries.Count > Capacity && node is not null)
        {
            LinkedListNode<string>? next = node.Next;
            if (_entries[node.Value].Status is not (OutcomeStatus.Queued or OutcomeStatus.Running))
            {
                _entries.Remove(node.Value);
                _order.Remove(node);
                Remember(node.Value);
            }

            node = next;
        }
    }

    private void Remember(string evicted)
    {
        _evicted.Add(evicted);
        _evictedOrder.Enqueue(evicted);
        if (_evictedOrder.Count > EvictedMemory)
        {
            _evicted.Remove(_evictedOrder.Dequeue());
        }
    }

    private static OutcomeGetData Copy(OutcomeGetData entry) => new() { Status = entry.Status, Operation = entry.Operation, Reason = entry.Reason };
}
