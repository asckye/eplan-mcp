namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The invokes received and not yet answered, by request ID, so that <c>operation.cancel</c> on another connection can
/// flag one (M4 design 4.3). Shared by every connection; thread-safe.
/// </summary>
internal sealed class RunningOperations
{
    private readonly object _gate = new();
    private readonly Dictionary<string, OperationContext> _contexts = new(StringComparer.Ordinal);

    /// <summary>Tracks <paramref name="context"/>; while another invoke with its request ID runs, that one keeps the ID.</summary>
    public void Add(OperationContext context)
    {
        lock (_gate)
        {
            if (!_contexts.ContainsKey(context.RequestId))
            {
                _contexts.Add(context.RequestId, context);
            }
        }
    }

    public void Remove(OperationContext context)
    {
        lock (_gate)
        {
            if (_contexts.TryGetValue(context.RequestId, out OperationContext? tracked) && ReferenceEquals(tracked, context))
            {
                _contexts.Remove(context.RequestId);
            }
        }
    }

    /// <summary>Flags the invoke with this request ID; false when there is none.</summary>
    public bool Cancel(string requestId)
    {
        OperationContext? context;
        lock (_gate)
        {
            _contexts.TryGetValue(requestId, out context);
        }

        context?.Cancel();
        return context is not null;
    }
}
