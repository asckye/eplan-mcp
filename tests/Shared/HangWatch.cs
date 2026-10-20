using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Microsoft.Testing.Platform.Builder;
using Microsoft.Testing.Platform.Extensions;
using Microsoft.Testing.Platform.Extensions.Messages;

namespace EplanMcp.Tests;

/// <summary>
/// Names the tests still running after two minutes, so a CI run that a hang stopped says which test hung. xUnit's own
/// <c>--long-running</c> report is a diagnostic message that <c>dotnet test</c> does not show. Only active when
/// <c>EPLANMCP_HANG_REPORT_DIR</c> names a directory (CI sets it to the test results): every 30 seconds the tests
/// running longer than the limit are written to <c>hung-tests-&lt;assembly&gt;.txt</c> there. A test counts as running
/// from xUnit's in-progress message (before the test class is created) to its result (after it is disposed).
/// Registered in every test assembly by tests/Directory.Build.props.
/// </summary>
internal sealed class HangWatch : IDataConsumer, IDisposable
{
    private static readonly TimeSpan _limit = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, (string Name, long Started)> _running = new();
    private readonly string _reportPath;
    private readonly Timer _timer;

    private HangWatch(string reportPath)
    {
        _reportPath = reportPath;
        _timer = new Timer(_ => Report(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public Type[] DataTypesConsumed => [typeof(TestNodeUpdateMessage)];

    public string Uid => nameof(HangWatch);

    public string Version => "1.0.0";

    public string DisplayName => "Hang watch";

    public string Description => "Writes the tests still running after two minutes to EPLANMCP_HANG_REPORT_DIR.";

    /// <summary>Called by the generated SelfRegisteredExtensions (TestingPlatformBuilderHook item).</summary>
    public static void AddExtensions(ITestApplicationBuilder builder, string[] args)
    {
        string? directory = Environment.GetEnvironmentVariable("EPLANMCP_HANG_REPORT_DIR");
        if (!string.IsNullOrEmpty(directory))
        {
            string name = $"hung-tests-{Assembly.GetExecutingAssembly().GetName().Name}.txt";
            builder.TestHost.AddDataConsumer(_ => new HangWatch(Path.Combine(directory, name)));
        }
    }

    public Task<bool> IsEnabledAsync() => Task.FromResult(true);

    public Task ConsumeAsync(IDataProducer dataProducer, IData value, CancellationToken cancellationToken)
    {
        if (value is TestNodeUpdateMessage { TestNode: var test })
        {
            switch (test.Properties.SingleOrDefault<TestNodeStateProperty>())
            {
                case InProgressTestNodeStateProperty:
                    _running[test.Uid.Value] = (test.DisplayName, Stopwatch.GetTimestamp());
                    break;
                case null or DiscoveredTestNodeStateProperty:
                    break;
                default:
                    _running.TryRemove(test.Uid.Value, out _);
                    break;
            }
        }

        return Task.CompletedTask;
    }

    public void Dispose() => _timer.Dispose();

    private void Report()
    {
        long now = Stopwatch.GetTimestamp();
        string[] lines = _running.Values
            .Select(test => (test.Name, Elapsed: Stopwatch.GetElapsedTime(test.Started, now)))
            .Where(test => test.Elapsed > _limit)
            .OrderByDescending(test => test.Elapsed)
            .Select(test => string.Create(CultureInfo.InvariantCulture, $"{test.Name} still running after {test.Elapsed:hh\\:mm\\:ss}"))
            .ToArray();
        if (lines.Length == 0)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_reportPath)!);
            File.WriteAllLines(_reportPath, lines);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A report that cannot be written must not crash the test run; the next tick tries again.
        }
    }
}
