using System.Security.Cryptography;
using System.Text.Json;
using EplanMcp.Host.Plans;
using EplanMcp.Packs;

namespace EplanMcp.Host.Packs;

/// <summary>On-demand plugin state snapshots; cooperating writers share a lock and compare raw-content revisions.</summary>
internal sealed class PluginStateStore(HostDataLocations locations)
{
    public const int MaxFileBytes = 1024 * 1024;
    private readonly PlanPaths _paths = new(locations.Root);
    private string FilePath => _paths.Check(Path.Combine(locations.State, "plugins.json"));

    public static PluginState Parse(byte[] bytes)
    {
        if (bytes.Length > MaxFileBytes)
        { throw new PackFormatException("plugins.json exceeds 1 MiB."); }
        try
        {
            PluginState state = JsonSerializer.Deserialize<PluginState>(bytes, PackJson.Options)
                ?? throw new JsonException("Plugin state cannot be null.");
            if (state.Version != 1 || state.Enabled.Count > 2000 || state.Libraries.Count > 128)
            { throw new JsonException("Plugin state requires version 1, at most 2000 enabled plugins and 128 libraries."); }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (PluginIdentity plugin in state.Enabled)
            {
                if (plugin is null || plugin.Id.Any(char.IsControl) || !PackManifestValidator.IsId(plugin.Id)
                    || plugin.Version.Length > 128 || plugin.Version.Any(char.IsControl) || !SemanticVersion.TryParse(plugin.Version, out _) || !ids.Add(plugin.Id))
                { throw new JsonException("Enabled plugins require unique IDs and exact SemVer versions."); }
            }
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RegisteredPluginLibrary library in state.Libraries)
            {
                if (library is null || library.Name.Any(char.IsControl) || !PackManifestValidator.IsId(library.Name) || !names.Add(library.Name)
                    || library.Path.Length > 4096 || library.Path.Any(char.IsControl) || !Path.IsPathFullyQualified(library.Path))
                { throw new JsonException("Registered libraries require unique names and absolute directory paths (UNC is allowed)."); }
                _ = Path.GetFullPath(library.Path);
            }
            return state;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException)
        { throw new PackFormatException("plugins.json is invalid: " + ex.Message, ex); }
    }

    public async Task<(PluginState State, string? Revision)> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, useAsync: true);
            if (stream.Length > MaxFileBytes)
            { throw new PackFormatException("plugins.json exceeds 1 MiB."); }
            byte[] bytes = new byte[(int)stream.Length];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            return (Parse(bytes), Hash(bytes));
        }
        catch (FileNotFoundException) { return (PluginState.Empty, null); }
        catch (DirectoryNotFoundException) { return (PluginState.Empty, null); }
    }

    public async Task<string> WriteAsync(PluginState state, string? expectedRevision, CancellationToken cancellationToken)
    {
        byte[] bytes = PackJson.Serialize(state);
        _ = Parse(bytes);
        using FileStream held = await FileLock.AcquireAsync(_paths.Check(Path.Combine(locations.State, ".plugins.lock")), TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false)
            ?? throw new PackFormatException("Plugin state is busy; read it and try again.");
        if ((await ReadAsync(cancellationToken).ConfigureAwait(false)).Revision != expectedRevision)
        { throw new PackFormatException("Plugin state revision changed; read it and try again."); }
        string temporary = FilePath + $".partial-{Guid.NewGuid():N}";
        try
        {
            using (var stream = new FileStream(_paths.Check(temporary), FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(_paths.Check(temporary), FilePath, overwrite: true);
            return Hash(bytes);
        }
        finally { File.Delete(_paths.Check(temporary)); }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
