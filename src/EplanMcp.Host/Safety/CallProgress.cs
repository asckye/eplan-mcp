using ModelContextProtocol;

namespace EplanMcp.Host.Safety;

/// <summary>
/// The progress the client sees of one tool call, which may wait several times in turn: a write for the engineer's
/// confirmation and approval, then for EPLAN; an export only for EPLAN. MCP wants every report to increase on the last, but each wait counts from zero; so each
/// reports through a lane of its own (<see cref="Lane"/>), whose values, total included, are moved past the last one
/// the client saw when the lane first reports. A report that would still not increase is dropped. Thread-safe:
/// <see cref="_lock"/> guards <see cref="_last"/>, and reports are passed on under it, in order.
/// </summary>
internal sealed class CallProgress
{
    private readonly Lock _lock = new();
    private readonly IProgress<ProgressNotificationValue> _client;
    private float? _last;

    private CallProgress(IProgress<ProgressNotificationValue> client) => _client = client;

    /// <summary>
    /// The progress of the tool's <c>IProgress</c> parameter; null when there is none. The SDK binds one that drops every
    /// report when the client sent no progress token.
    /// </summary>
    public static CallProgress? For(IProgress<ProgressNotificationValue>? client) => client is null ? null : new CallProgress(client);

    /// <summary>A new lane, for the next wait of the call.</summary>
    public IProgress<ProgressNotificationValue> Lane() => new LaneProgress(this);

    private sealed class LaneProgress(CallProgress call) : IProgress<ProgressNotificationValue>
    {
        private float? _offset;

        public void Report(ProgressNotificationValue value)
        {
            lock (call._lock)
            {
                _offset ??= call._last + 1 ?? 0;
                float progress = _offset.Value + value.Progress;
                if (progress <= call._last)
                {
                    return;
                }

                call._last = progress;
                call._client.Report(new ProgressNotificationValue { Progress = progress, Total = value.Total + _offset, Message = value.Message });
            }
        }
    }
}
