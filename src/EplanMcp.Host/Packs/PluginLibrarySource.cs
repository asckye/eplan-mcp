using System.Security;
using EplanMcp.Host.Plans;
using EplanMcp.Packs;

namespace EplanMcp.Host.Packs;

/// <summary>Reads directory indexes only; archive integrity is checked by the installer.</summary>
internal sealed class PluginLibrarySource(HostDataLocations locations, PluginStateStore state)
{
    internal string ApplicationDirectory { get; init; } = AppContext.BaseDirectory;

    public async Task<(IReadOnlyList<PluginLibraryReadResult> Libraries, IReadOnlyList<string> Warnings)> ReadAsync(CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var candidates = new List<(string Name, string Type, string Folder, string Root)>
        {
            ("builtin", "builtin", Path.Combine(ApplicationDirectory, "plugin-libraries", "builtin"), ApplicationDirectory),
        };
        try
        {
            string root = new PlanPaths(locations.Root).Check(locations.PluginLibraries);
            candidates.AddRange(Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal)
                .Select(folder => (Path.GetFileName(folder), "local", folder, locations.Root)));
        }
        catch (Exception ex) when (Unreadable(ex))
        {
            warnings.Add("The local plugin library directory could not be read.");
        }
        try
        {
            (PluginState current, _) = await state.ReadAsync(cancellationToken).ConfigureAwait(false);
            candidates.AddRange(current.Libraries.Select(library => (library.Name, "shared", library.Path, library.Path)));
        }
        catch (Exception ex) when (Unreadable(ex))
        {
            warnings.Add("Plugin state could not be read; registered shared libraries are unavailable.");
        }

        var results = new List<PluginLibraryReadResult>();
        foreach ((string name, string type, string folder, string root) in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string file = new PlanPaths(root).Check(Path.Combine(folder, PluginLibraryReader.FileName));
                using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, useAsync: true);
                var buffer = new byte[PluginLibraryReader.MaxFileBytes + 1];
                int count = await input.ReadAtLeastAsync(buffer, buffer.Length, false, cancellationToken).ConfigureAwait(false);
                PluginLibraryIndex index = PluginLibraryReader.Read(buffer[..count]);
                results.Add(new(name, type, folder, index, []));
            }
            catch (Exception ex) when (Unreadable(ex))
            {
                results.Add(new(name, type, folder, null, ["Library index is missing, unreadable or invalid; other libraries remain available."]));
            }
        }
        return (results, warnings);
    }

    private static bool Unreadable(Exception ex) => ex is IOException or UnauthorizedAccessException
        or SecurityException or PackFormatException or ArgumentException or NotSupportedException;
}
