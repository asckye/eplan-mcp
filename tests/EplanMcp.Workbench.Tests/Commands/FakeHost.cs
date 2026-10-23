using System.Reflection;
using System.Text.Json.Nodes;
using EplanMcp.Workbench.Commands;

namespace EplanMcp.Workbench.Tests.Commands;

/// <summary>
/// The Host process for the tests: every call is recorded, and answered by <see cref="Answer"/> (an exit code and an
/// output, or an exception), or held until the test releases it with <see cref="Hold"/>.
/// </summary>
internal sealed class FakeHost : IHostRunner
{
    private TaskCompletionSource<HostRun>? _held;

    public string Executable { get; set; } = @"C:\Users\engineer\EplanMcp\app\eplanmcp.exe";

    /// <summary>The argument lists and timeouts of the runs so far.</summary>
    public List<(string[] Arguments, TimeSpan Timeout)> Runs { get; } = [];
    public List<string?> Inputs { get; } = [];

    public Task<HostRun> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, string? input, CancellationToken cancellationToken)
    {
        Inputs.Add(input);
        return RunAsync(arguments, timeout, cancellationToken);
    }

    /// <summary>What the next runs give, by their arguments; the default is an error.</summary>
    public Func<string[], HostRun> Answer { get; set; } = _ => throw new HostCommandException(HostFailure.NotStarted, "No answer was set.");

    /// <summary>Makes the next run wait until the returned source is completed.</summary>
    public TaskCompletionSource<HostRun> Hold() => _held = new TaskCompletionSource<HostRun>(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<HostRun> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Runs.Add(([.. arguments], timeout));
        if (_held is { } held)
        {
            _held = null;
            return held.Task.WaitAsync(cancellationToken);
        }

        return Task.FromResult(Answer([.. arguments]));
    }
}

/// <summary>The Host commands' JSON snapshots (tests/Contracts.Snapshots), embedded in this assembly.</summary>
internal static class Snapshots
{
    /// <summary>The whole snapshot <paramref name="name"/>, such as <c>doctor-json</c>.</summary>
    public static string Read(string name)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Snapshots/{name}.verified.json")
            ?? throw new InvalidOperationException($"The snapshot {name} is not embedded; check EplanMcp.Workbench.Tests.csproj.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The example <paramref name="example"/> of a snapshot that holds several, as the Host writes it.</summary>
    public static string Example(string name, string example) => JsonNode.Parse(Read(name))![example]!.ToJsonString();

    /// <summary>The names of the examples of a snapshot that holds several.</summary>
    public static IEnumerable<string> Examples(string name) => JsonNode.Parse(Read(name))!.AsObject().Select(p => p.Key);
}
