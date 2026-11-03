using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Plans;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tools;

/// <summary>Consumes effective values and their winning plugin without changing the resolved configuration.</summary>
internal static class PluginConsumerDefaults
{
    internal static JsonElement? Value(PluginResolution resolution, string key) => resolution.Values.EnumerateArray()
        .Where(item => item.GetProperty("key").GetString() == key).Select(item => (JsonElement?)item.GetProperty("value"))
        .FirstOrDefault(value => value?.ValueKind is not null and not JsonValueKind.Null);

    internal static string[] Plugins(PluginResolution resolution, params string[] keys) => [.. resolution.Values.EnumerateArray()
        .Where(item => keys.Contains(item.GetProperty("key").GetString(), StringComparer.Ordinal))
        .Select(item => item.GetProperty("winner")).Where(winner => winner.ValueKind == JsonValueKind.Object)
        .Select(winner => winner.TryGetProperty("plugin", out JsonElement plugin) ? plugin.GetString()
            : winner.GetProperty("source").TryGetProperty("resource", out JsonElement resource) ? resource.GetString()?.Split(':')[0] : null)
        .OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    internal static Func<CancellationToken, Task>? BeforeExecute(PackLibrary packs, PluginResolution resolution, string[] usedPlugins, ICollection<string>? warnings = null) =>
        usedPlugins.Length == 0 ? null : async cancellationToken =>
        {
            bool changed = false;
            try
            {
                (_, string? revision) = await new PluginStateStore(packs.Locations).ReadAsync(cancellationToken).ConfigureAwait(false);
                changed = revision != resolution.StateRevision;
            }
            catch (Exception ex) when (PluginConfigurationReader.Unreadable(ex))
            { warnings?.Add("Plugin state is unreadable; its current revision could not be checked."); }
            if (resolution.BindingProject is { } project)
            {
                try
                {
                    _ = new PlanPaths(packs.Locations.Root).Check(Path.Combine(packs.Workspaces.FolderOf(project), ProjectBinding.FileName));
                    ProjectBinding? binding = await packs.Workspaces.ReadAsync(project, cancellationToken).ConfigureAwait(false);
                    changed |= binding?.Revision != resolution.BindingRevision;
                }
                catch (Exception ex) when (PluginConfigurationReader.Unreadable(ex))
                { warnings?.Add("Project plugin binding is unreadable; its current revision could not be checked."); }
            }
            if (changed)
            {
                throw new BridgeCallException(new BridgeError
                {
                    Reason = ErrorReason.PlanStale,
                    Execution = ExecutionState.NotStarted,
                    Message = "Enabled plugins changed after planning; create a new plan.",
                });
            }
        };
}
