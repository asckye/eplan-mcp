using System.Security;
using EplanMcp.Packs;

namespace EplanMcp.Host.Packs;

internal static class PluginStateQuery
{
    public static async Task<(PluginState? State, string? Revision, IReadOnlyList<string> Warnings)> ReadAsync(
        PluginStateStore store, CancellationToken cancellationToken)
    {
        try
        {
            var (state, revision) = await store.ReadAsync(cancellationToken).ConfigureAwait(false);
            return (state, revision, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or PackFormatException)
        {
            return (null, null, ["Plugin state could not be read; global enablement is unknown. Repair state/plugins.json and read again."]);
        }
    }
}
