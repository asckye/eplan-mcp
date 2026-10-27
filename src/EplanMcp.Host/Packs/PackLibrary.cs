using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;

namespace EplanMcp.Host.Packs;

/// <summary>
/// The engineering packs of the data root for the pack tools (M6): development packs, installed versions, and the
/// audit record every local-data change writes, <c>pack_change</c> (M6 design 4.5.1): one before the change, which it
/// does not start without, and one after it. The stores keep no state between calls, so one instance serves all.
/// </summary>
internal sealed class PackLibrary(HostDataLocations locations, HostConfig config, AuditLog audit, TimeProvider clock)
{
    public const string AuditKind = "pack_change";

    public HostDataLocations Locations => locations;

    public HostConfig Config => config;

    public TimeProvider Clock => clock;

    public DevPackStore Dev { get; } = new(locations.PacksDev);

    public PackStore Installed { get; } = new(locations.Packs);

    /// <summary>The project bindings and their locks (M6 design 4.1.5).</summary>
    public WorkspaceStore Workspaces => new(locations.Workspaces, Installed);

    /// <summary>Internal maintenance only: unreadable bindings or frozen closures block every deletion.</summary>
    internal async Task RemoveUnusedVersionAsync(string id, string version, CancellationToken cancellationToken)
    {
        if (!PackManifestValidator.IsId(id) || !SemanticVersion.TryParse(version, out _))
        { throw new PackFormatException("/packs: cleanup requires a pack id and exact version."); }
        var paths = new PlanPaths(locations.Root);
        string folder = paths.Check(Installed.FolderOf(id, version));
        using FileStream held = await FileLock.AcquireAsync(paths.Check(Path.Combine(Installed.Root, PackStore.LockFileName)), Installed.LockWait, cancellationToken).ConfigureAwait(false)
            ?? throw new PackFormatException("/packs: the pack store is busy; cleanup is blocked.");
        // Validate the complete traversal before AllAsync, which reports individual unreadable binding files.
        _ = paths.Files(locations.Workspaces).ToArray();
        (IReadOnlyList<ProjectBinding> bindings, int unreadable) = await Workspaces.AllAsync(cancellationToken).ConfigureAwait(false);
        if (unreadable != 0)
        { throw new PackFormatException("/bindings: at least one binding is unreadable; repair it before cleanup."); }
        IReadOnlySet<(string Id, string Version)> occupied = await PackOccupancy.ResolveAsync(bindings, ReadAsync, cancellationToken).ConfigureAwait(false);
        if (occupied.Contains((id, version)))
        { throw new PackFormatException("/locks: this version is locked by a project or its dependencies; cleanup is blocked."); }
        if (!Directory.Exists(folder))
        { return; }
        string[] files = paths.Files(folder).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        string requestId = Guid.NewGuid().ToString("N");
        var details = new JsonObject { ["action"] = "cleanup", ["pack"] = id, ["version"] = version };
        await AuditAsync(requestId, "pack_cleanup", "started", details, changed: false, cancellationToken).ConfigureAwait(false);
        foreach (string file in files)
        { File.SetAttributes(file, FileAttributes.Normal); }
        Directory.Delete(paths.Check(folder), recursive: true);
        await AuditAsync(requestId, "pack_cleanup", "committed", details, changed: true, CancellationToken.None).ConfigureAwait(false);

        async Task<(PackManifest, PackLock)> ReadAsync(string pack, string exact, CancellationToken token)
        {
            PackCheckResult check = await PackVerifier.CheckDirectoryAsync(paths.Check(Installed.FolderOf(pack, exact)), token).ConfigureAwait(false);
            if (!check.Ok || check.Manifest is not { } manifest)
            { throw new PackFormatException("/locks: an installed pack is unreadable or invalid; cleanup is blocked."); }
            PackLock? frozen = check.Lock;
            if (frozen is null && manifest.Dependencies.Count == 0)
            { frozen = PackLock.Create(new() { Id = pack, Version = exact, Digest = Installed.InstalledDigest(pack, exact)! }, []); }
            return (manifest, frozen ?? throw new PackFormatException("/locks: a dependency lock is missing; cleanup is blocked."));
        }
    }

    /// <summary>
    /// The folder and shown version of pack <paramref name="id"/> at <paramref name="version"/>: <c>dev</c>, a SemVer
    /// version, or null for the development pack when there is one, else the highest installed version. Null when
    /// there is no such pack or version.
    /// </summary>
    public (string Folder, string Version)? Locate(string id, string? version)
    {
        string dev = Dev.FolderOf(id);
        if (version is null or AssetNames.Dev && File.Exists(Path.Combine(dev, PackManifest.FileName)))
        {
            return (dev, AssetNames.Dev);
        }

        IReadOnlyList<SemanticVersion> installed = Installed.Versions(id);
        string? chosen = version is AssetNames.Dev ? null : version ?? (installed.Count > 0 ? installed[0].ToString() : null);
        return chosen is not null && Installed.InstalledDigest(id, chosen) is not null ? (Installed.FolderOf(id, chosen), chosen) : null;
    }

    /// <summary>Whether <paramref name="version"/> is a version argument: absent, <c>dev</c> or SemVer.</summary>
    public static bool IsVersion(string? version) => version is null or AssetNames.Dev || SemanticVersion.TryParse(version, out _);

    /// <summary>RESOURCE_NOT_FOUND for a pack or version that is not there.</summary>
    public static BridgeError NotFound(string pack, string? version) => new()
    {
        Reason = ErrorReason.ResourceNotFound,
        Message = $"No pack '{pack}'{(version is null ? string.Empty : $" at version '{version}'")} is in EplanMcp's data folder. Call eplan_pack_list to see the packs and their versions.",
        Metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["pack"] = pack, ["version"] = version ?? string.Empty },
    };

    /// <summary>What manifests name as their maker.</summary>
    public static string CreatedBy => $"eplanmcp {HostInfo.Version}";

    /// <summary>
    /// Appends a pack_change record for <paramref name="phase"/> (<c>started</c>, <c>committed</c>, <c>failed</c>);
    /// throws <see cref="BridgeCallException"/> with AUDIT_UNAVAILABLE when it cannot (SEC-09), saying whether the pack
    /// was <paramref name="changed"/> already.
    /// </summary>
    public async Task AuditAsync(string requestId, string tool, string phase, JsonObject details, bool changed, CancellationToken cancellationToken)
    {
        var entry = new JsonObject { ["kind"] = AuditKind, ["requestId"] = requestId, ["tool"] = tool, ["phase"] = phase };
        foreach ((string name, JsonNode? value) in details)
        {
            entry[name] = value?.DeepClone();
        }

        try
        {
            await audit.AppendAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        catch (AuditUnavailableException)
        {
            throw new BridgeCallException(new BridgeError
            {
                Reason = ErrorReason.AuditUnavailable,
                Message = changed
                    ? "The pack was changed, but its audit record could not be written. Ask the engineer to run 'eplanmcp doctor' and 'eplanmcp audit verify'."
                    : "The audit log cannot be written, so nothing was changed. Ask the engineer to run 'eplanmcp doctor'.",
            });
        }
    }

    /// <summary>Records that a change failed; a log that cannot be written then is reported by the next change.</summary>
    public async Task AuditFailedAsync(string requestId, string tool, string reason, CancellationToken cancellationToken)
    {
        try
        {
            await AuditAsync(requestId, tool, "failed", new JsonObject { ["reason"] = reason }, changed: false, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException)
        {
            // The started record is there; audit verify and the next write report the log.
        }
    }

    /// <summary>The tool error for a development pack that was not changed.</summary>
    public static BridgeError Error(DevPackException ex, string pack) => ex.Error switch
    {
        DevPackError.Conflict => new BridgeError
        {
            Reason = ErrorReason.AlreadyExists,
            Message = $"Development pack '{pack}' holds these resources already, with other content, so nothing was imported. Once the engineer agrees, call again with replace: true.",
            Metadata = Problems(ex.Problems),
        },
        DevPackError.Busy => new BridgeError
        {
            Reason = ErrorReason.Timeout,
            Retryable = true,
            Message = $"Another EplanMcp is changing development pack '{pack}'; call again in a moment.",
        },
        _ => new BridgeError
        {
            Reason = ErrorReason.PackInvalid,
            Message = $"Development pack '{pack}' fails its check, so it was not changed: {ResultShaper.CleanText(ex.Problems.Count > 0 ? ex.Problems[0].ToString() : "unknown")}. eplan_pack_get lists every problem; ask the engineer to repair the folder packs-dev/{pack} in EplanMcp's data folder.",
            Metadata = Problems(ex.Problems),
        },
    };

    /// <summary>Problems as error metadata: one per line, at most 20, pack text cleaned (mcp-tool-design 7.3).</summary>
    public static Dictionary<string, string> Problems(IEnumerable<PackProblem> problems) => new(StringComparer.Ordinal)
    {
        ["problems"] = string.Join("\n", problems.Take(20).Select(p => ResultShaper.CleanText(p.ToString()))),
    };
}
