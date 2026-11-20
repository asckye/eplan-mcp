using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EplanMcp.Host.Safety;

/// <summary>
/// The configuration a write is decided with, taking <c>writesOutsideScratch</c> from <c>config.json</c> as it is at
/// that moment (ADR-0061). The engineer changes that value in the Workbench while Hosts are running; a Host that kept
/// its start-up value would go on allowing writes after the engineer turned approval back on. Only this value follows
/// the file; scratch roots and every other setting stay as they were when the Host started.
/// </summary>
internal sealed class LiveWritePolicy(HostConfig startup, HostDataLocations locations)
{
    public HostConfig Current()
    {
        string text;
        try
        {
            text = File.ReadAllText(locations.ConfigFile);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // No file: nothing was changed since the start, and a Host configured in code keeps its configuration.
            return startup;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Strictest();
        }

        (HostConfig? now, string? problem) = HostConfig.Parse(text, locations);
        return now is null || problem is not null ? Strictest() : With(now.WritesOutsideScratch);
    }

    /// <summary>A file that cannot be read or is not valid never allows: approval, or denial where that was configured.</summary>
    private HostConfig Strictest() =>
        With(startup.WritesOutsideScratch == OutsideScratchWrites.Deny ? OutsideScratchWrites.Deny : OutsideScratchWrites.Approve);

    private HostConfig With(OutsideScratchWrites value) => value == startup.WritesOutsideScratch ? startup : startup with { WritesOutsideScratch = value };
}

internal static class WritePipelineSetup
{
    /// <summary>The pipeline together with the policy it decides with: one without the other would keep the start-up value.</summary>
    public static void AddWritePipeline(this IServiceCollection services)
    {
        services.TryAddSingleton<LiveWritePolicy>();
        services.TryAddSingleton<WritePipeline>();
    }
}
