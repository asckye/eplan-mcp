using System.Collections.Concurrent;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Packs;

namespace EplanMcp.Host.Packs;

/// <summary>Owns in-memory handles in a concurrent map; only verified referenced resources enter the Bridge seam.</summary>
internal sealed class ReferenceCopyService(IBridgeClient bridge, string scratchRoot, IReadOnlyDictionary<string, string> roots)
{
    private readonly ConcurrentDictionary<string, (PackResource Resource, string Path)> _owned = new(StringComparer.Ordinal);
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
        if (!_owned.TryAdd(copy.Handle, (resource, copy.Path)))
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
        if (string.IsNullOrEmpty(handle) || !_owned.TryGetValue(handle, out var copy))
        { throw Invalid(); }
        ProjectListData result = await bridge.InvokeAsync<ReferenceCloseArgs, ProjectListData>(OperationNames.ReferenceClose, new() { Handle = handle }, cancellationToken).ConfigureAwait(false);
        _owned.TryRemove(handle, out _);
        await VerifyAsync(copy.Resource, CancellationToken.None).ConfigureAwait(false);
        return result;
    }
    public async Task<ReferenceExtractionResult> ExtractAsync(string handle, IReadOnlyList<string> pages, IReferenceExtractor extractor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(handle) || !_owned.TryGetValue(handle, out var copy))
        { throw Invalid(); }
        string[] selected = [.. pages];
        if (selected.Length is < 1 or > 50 || selected.Any(string.IsNullOrWhiteSpace) || selected.Distinct(StringComparer.Ordinal).Count() != selected.Length)
        { throw new BridgeCallException(new BridgeError { Reason = ErrorReason.InvalidArgument, Message = "Select 1 to 50 distinct full page names before extraction.", Metadata = new() { ["pointer"] = "/scope/pages" } }); }
        await VerifyAsync(copy.Resource, cancellationToken).ConfigureAwait(false);
        try
        {
            BridgeStatus status = await bridge.GetStatusAsync(true, cancellationToken).ConfigureAwait(false);
            if (status.Handshake?.Eplan.ApiExtensionLicence != true)
            { return ReferenceExtractionResult.Missing(copy.Resource.Path, "licence:APIExtension", "Connect to EPLAN with a confirmed API Extension licence, then retry extraction; an unknown licence is insufficient."); }
            foreach (string operation in extractor.RequiredOperations)
            {
                if (!BridgeClient.Supports(status.Handshake, operation))
                { return ReferenceExtractionResult.Missing(copy.Resource.Path, "op:" + operation, "The reference reader needs a Bridge capability; install the current add-in and restart EPLAN."); }
            }
            ProjectListData open = await bridge.InvokeAsync<ProjectListArgs, ProjectListData>(OperationNames.ProjectList, new(), cancellationToken).ConfigureAwait(false);
            if (!open.Projects.Any(p => p.ReadOnly && Safety.ScratchPaths.Canonical(p.Path.EndsWith(".elk", StringComparison.OrdinalIgnoreCase) ? p.Path : p.Path + ".elk").Equals(copy.Path, StringComparison.OrdinalIgnoreCase)))
            { throw Invalid(); }
            return await extractor.ExtractAsync(copy.Path, copy.Resource.Path, selected, cancellationToken).ConfigureAwait(false);
        }
        finally
        { await VerifyAsync(copy.Resource, CancellationToken.None).ConfigureAwait(false); }
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
    private static BridgeCallException Invalid() => new(new BridgeError { Reason = ErrorReason.InvalidArgument, Message = "Use a referenced project resource and a local scratch root without links; extract from or close only this service's read-only copy handles." });
}
