using ModelContextProtocol;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>Keeps every progress report as it is made, on the reporting thread (unlike <see cref="Progress{T}"/>).</summary>
internal sealed class RecordedProgress : IProgress<ProgressNotificationValue>
{
    private readonly Lock _lock = new();
    private readonly List<ProgressNotificationValue> _values = [];

    public List<ProgressNotificationValue> Values
    {
        get
        {
            lock (_lock)
            {
                return [.. _values];
            }
        }
    }

    public void Report(ProgressNotificationValue value)
    {
        lock (_lock)
        {
            _values.Add(value);
        }
    }
}
