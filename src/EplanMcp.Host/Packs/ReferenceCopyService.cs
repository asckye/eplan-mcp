using System.Collections.Concurrent;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Packs;

namespace EplanMcp.Host.Packs;

/// <summary>Owns in-memory handles in a concurrent map; only verified referenced resources enter the Bridge seam.</summary>
internal sealed class ReferenceCopyService(IBridgeClient bridge, string scratchRoot, IReadOnlyDictionary<string, string> roots)
{
    private readonly ConcurrentDictionary<string, PackResource> _owned = new(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, string> _roots = roots.ToDictionary(StringComparer.Ordinal);

    public static string Source(PackResource resource, IReadOnlyDictionary<string, string> roots) =>
        resource.Storage == ResourceStorage.Referenced && resource.Reference is { } reference && PackPath.Problem(reference.Path) is null
        && resource.Kind is ResourceKind.Project or ResourceKind.ProjectBackup && roots.TryGetValue(reference.Root, out string? root)
        && Plain(root)
        && Path.GetExtension(reference.Path).Equals(resource.Kind == ResourceKind.Project ? ".elk" : ".zw1", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(root, reference.Path.Replace('/', Path.DirectorySeparatorChar)) : throw Invalid();

    public async Task<ReferenceCopyData> PrepareAsync(PackResource resource, CancellationToken cancellationToken)
    {
        string source = Local(Source(resource, _roots));
        await VerifyAsync(resource, cancellationToken).ConfigureAwait(false);
        string folder = Local(Path.Combine(Local(scratchRoot), "reference-" + Guid.NewGuid().ToString("N")));
        if (Directory.Exists(folder) || File.Exists(folder) || folder.StartsWith(Path.ChangeExtension(source, ".edb") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        { throw Invalid(); }
        Directory.CreateDirectory(folder);
        var args = new ReferencePrepareArgs { Source = source, Destination = Local(Path.Combine(folder, "Reference.elk")), ScratchRoot = Local(scratchRoot) };
        ReferenceCopyData copy = await bridge.InvokeAsync<ReferencePrepareArgs, ReferenceCopyData>(OperationNames.ReferencePrepare, args, cancellationToken).ConfigureAwait(false);
        if (!_owned.TryAdd(copy.Handle, resource))
        { throw Invalid(); }
        try
        { await VerifyAsync(resource, cancellationToken).ConfigureAwait(false); }
        catch
        {
            await CloseAsync(copy.Handle, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        return copy;
    }
    public async Task<ProjectListData> CloseAsync(string handle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(handle) || !_owned.TryGetValue(handle, out PackResource? resource))
        { throw Invalid(); }
        ProjectListData result = await bridge.InvokeAsync<ReferenceCloseArgs, ProjectListData>(OperationNames.ReferenceClose, new() { Handle = handle }, cancellationToken).ConfigureAwait(false);
        _owned.TryRemove(handle, out _);
        await VerifyAsync(resource, CancellationToken.None).ConfigureAwait(false);
        return result;
    }
    private async Task VerifyAsync(PackResource resource, CancellationToken cancellationToken)
    {
        Local(Source(resource, _roots));
        if (await PackVerifier.ReferenceProblemAsync(resource, _roots, cancellationToken).ConfigureAwait(false) is not null)
        { throw new BridgeCallException(new BridgeError { Reason = ErrorReason.PlanStale, Message = "The reference source differs from its pack resource; import it again before extraction." }); }
    }
    private static bool Plain(string path) => Path.IsPathFullyQualified(path) && !path.StartsWith('\\')
        && (path.Length == 3 || PackPath.Problem(path[3..].Replace('\\', '/').TrimEnd('/')) is null);
    private static string Local(string path) => Plain(path)
        ? Safety.ScratchPaths.Canonical(new PlanPaths(Path.GetPathRoot(path)!).Check(path)) : throw Invalid();
    private static BridgeCallException Invalid() => new(new BridgeError { Reason = ErrorReason.InvalidArgument, Message = "Use a referenced project resource and a local scratch root without links; close only this service's copy handles." });
}
