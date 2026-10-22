using System.Security.Cryptography;
using System.Text;

namespace EplanMcp.Packs;

/// <summary>
/// The project bindings, <c>&lt;data root&gt;\workspaces\</c> (M6 design 3.3, 4.1.5): one folder per project, named by
/// the first 32 hex digits of the SHA-256 of its project key in upper case, because a key is a path, which cannot be a
/// folder name, and Windows paths ignore case; <c>binding.json</c> in it records the key itself. A change holds the
/// workspace's lock file and raises the revision. Installing a newer pack version changes no lock.
/// </summary>
public sealed class WorkspaceStore(string root, PackStore packs)
{
    private const string LockFileName = ".binding.lock";

    public string Root { get; } = root;

    /// <summary>How long a change waits for another one to release the workspace's lock before it gives up.</summary>
    public TimeSpan LockWait { get; init; } = TimeSpan.FromSeconds(30);

    public string FolderOf(string projectKey) =>
        Path.Combine(Root, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(projectKey.ToUpperInvariant())))[..32]);

    /// <summary>The project's binding, or null when it has none.</summary>
    public async Task<ProjectBinding?> ReadAsync(string projectKey, CancellationToken cancellationToken)
    {
        string file = Path.Combine(FolderOf(projectKey), ProjectBinding.FileName);
        return File.Exists(file)
            ? PackJson.Read<ProjectBinding>(
                await PackVerifier.ReadSmallAsync(file, ProjectBinding.FileName, cancellationToken).ConfigureAwait(false),
                ProjectBinding.FileName, "bindingVersion", ProjectBinding.CurrentBindingVersion)
            : null;
    }

    /// <summary>Locks the project to the installed <paramref name="version"/> of <paramref name="id"/>, replacing its lock of that id.</summary>
    public async Task<ProjectBinding> LockPackAsync(string projectKey, string id, string version, CancellationToken cancellationToken)
    {
        string digest = packs.InstalledDigest(id, version) ?? throw new PackFormatException($"{id} {version} is not installed; install it before locking a project to it.");
        string folder = FolderOf(projectKey);
        using FileStream held = await FileLock.AcquireAsync(Path.Combine(folder, LockFileName), LockWait, cancellationToken).ConfigureAwait(false)
            ?? throw new PackFormatException("The project's binding is being changed by another EplanMcp; try again when it is done.");
        ProjectBinding binding = await ReadAsync(projectKey, cancellationToken).ConfigureAwait(false)
            ?? new ProjectBinding { BindingVersion = ProjectBinding.CurrentBindingVersion, Project = projectKey, Revision = 0 };
        var pin = new PackReference { Id = id, Version = version, Digest = digest };
        binding = binding with
        {
            Revision = binding.Revision + 1,
            Locks = [.. binding.Locks.Where(l => l.Id != id).Append(pin).OrderBy(l => l.Id, StringComparer.Ordinal)],
        };
        string file = Path.Combine(folder, ProjectBinding.FileName);
        string partial = $"{file}.partial-{Guid.NewGuid():N}";
        await File.WriteAllBytesAsync(partial, PackJson.Serialize(binding), cancellationToken).ConfigureAwait(false);
        File.Move(partial, file, overwrite: true);
        return binding;
    }

    /// <summary>A problem for each lock whose installed version is gone or changed.</summary>
    public async Task<IReadOnlyList<PackProblem>> CheckAsync(string projectKey, CancellationToken cancellationToken)
    {
        ProjectBinding? binding = await ReadAsync(projectKey, cancellationToken).ConfigureAwait(false);
        return
        [
            .. (binding?.Locks ?? []).Where(l => packs.InstalledDigest(l.Id, l.Version) != l.Digest)
                .Select(l => new PackProblem($"{l.Id} {l.Version}", "is locked but no longer installed as it was")),
        ];
    }
}
