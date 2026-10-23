using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EplanMcp.Host.Plans;

internal sealed class PlanStoreCleanup(PlanStore store, ILogger<PlanStoreCleanup> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> _failed = LoggerMessage.Define(LogLevel.Warning, new EventId(1, "PlanCleanupFailed"), "Plan retention cleanup could not complete; admission will check again.");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromHours(1), store.Clock, stoppingToken).ConfigureAwait(false);
            try
            {
                await store.CleanupAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _failed(logger, ex);
            }
        }
    }
}
