using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Hosting;

namespace EplanMcp.Host.Http;

internal sealed record HttpServiceState(int ProcessId, DateTimeOffset ProcessStart, string Address, int Port);

/// <summary>Each HTTP host owns one discovery file; stale files are never evidence without matching process identity.</summary>
internal sealed class HttpServiceStateStore(HostDataLocations locations, IServer server) : IHostedLifecycleService, IDisposable
{
    private readonly string _file = Path.Combine(locations.State, FormattableString.Invariant($"http-{Environment.ProcessId}-{Guid.NewGuid():N}.json"));

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        var uri = new Uri(server.Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        var state = new HttpServiceState(Environment.ProcessId, StartOf(Environment.ProcessId)!.Value, uri.Host, uri.Port);
        Directory.CreateDirectory(locations.State);
        string temporary = _file + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, HostJson.Options));
            File.Move(temporary, _file);
        }
        finally
        {
            File.Delete(temporary);
        }

        return Task.CompletedTask;
    }

    public void Dispose() => File.Delete(_file);
    public Task StopAsync(CancellationToken cancellationToken) { Dispose(); return Task.CompletedTask; }
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static IEnumerable<HttpServiceState> Read(HostDataLocations locations) => Directory.Exists(locations.State)
        ? Directory.EnumerateFiles(locations.State, "http-*.json").Select(file => Parse(File.ReadAllText(file))) : [];

    internal static HttpServiceState Parse(string json)
    {
        HttpServiceState? state = JsonSerializer.Deserialize<HttpServiceState>(json, HostJson.Options);
        return state is { ProcessId: > 0 } && state.ProcessStart != default
            && HttpArguments.Parse(["--http", "--address", state.Address, "--port", state.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)]) is not null
            ? state : throw new JsonException("Invalid HTTP discovery state.");
    }

    public static DateTimeOffset? StartOf(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return process.HasExited ? null : new DateTimeOffset(process.StartTime.ToUniversalTime());
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null; // A stopped process is expected after an unclean shutdown; never delete from doctor.
        }
    }
}
