using System.Diagnostics;
using System.Globalization;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanApiTryDispatcher
{
    private void CheckLifecycle(ApiTryArgs args, JObject parameters, string[] shape)
    {
        if (ApiTryLifecycle.Effect(args.Group) is not { } effect)
        { return; }
        if (effect == "creates")
        {
            foreach ((JToken token, string type) in ArgumentObjects(shape, parameters).Where(p => p.Type == "Eplan.EplApi.DataModel.Page"))
            {
                if (ResolveObject(args, token, type, true, []).Value is not Page { IsValid: true } page)
                { throw new BridgeErrorException(ErrorReason.InvalidArgument, "Creation requires a valid page in the target scratch project."); }
                EplanNativePreconditions.Writable(page);
            }
        }
        else
        {
            if (ApiTryCatalog.Static(shape[1]))
            { return; }
            object target = ResolveObject(args, parameters["target"]!, ApiTryCatalog.Service(args.Group) ? "Eplan.EplApi.DataModel.Project" : shape[0], true, []).Value;
            if (target is not StorableObject { IsValid: true } obj || obj is Placement placement && !placement.IsPlaced)
            { throw new BridgeErrorException(ErrorReason.InvalidArgument, "Removal requires a valid, placed object in the target scratch project."); }
            EplanNativePreconditions.Writable(obj);
        }
    }

    private static object? LifecycleResult(ApiTryArgs args, object? target, object? result) =>
        args.Group.EndsWith(".Create", StringComparison.Ordinal) && !ApiTryCatalog.Service(args.Group) && result is null ? target : result;

    public Dictionary<string, string> Observe(ApiTryArgs args)
    {
        MainThread.Assert();
        var watch = Stopwatch.StartNew();
        var scan = new ApiTryLifecycleScan(args, () => watch.Elapsed);
        Project project = EplanProjects.ByPath(args.Project);
        using var finder = new DMObjectsFinder(project);
        try
        {
            return scan.Read((pages, check) => LifecycleCandidates(finder, project, pages, check),
                obj => ObjectKindDispatch.Kind(obj) is { } kind ? "object:" + kind + ":" + obj.ObjectIdentifier.ToString(CultureInfo.InvariantCulture) : null,
                obj =>
                {
                    DrawingLocator locator = Locator(args.Project, obj, out SnapshotObject identity);
                    return JsonConvert.SerializeObject(new { locator = new { project = locator.Project, page = locator.Page, type = locator.Type, oid = locator.Oid, digest = locator.Digest }, name = identity.Name, resources = scan.Touched(locator) ? ApiTryDispatch.LifecycleReadback(obj) : null });
                });
        }
        finally
        {
            // Counts and duration stay out of approval fingerprints; no project names or values enter this log.
            _log.Write(BridgeLogLevel.Information, "[" + OperationContext.Current.RequestId + "] Lifecycle observations: " + JsonConvert.SerializeObject(scan.Measurements()));
        }
    }

    private static IEnumerable<StorableObject> LifecycleCandidates(DMObjectsFinder finder, Project project, string[]? pages, Action check)
    {
        if (pages is null)
        {
            foreach (StorableObject obj in EplanNativePreconditions.Valid(ObjectKindDispatch.AllCandidates(finder, project, check)))
            { yield return obj; }
            yield break;
        }
        // A60/A62: page placements give exact runtime types; A36: connections are not placements.
        foreach (Page page in EplanNativePreconditions.Valid(finder.GetPages(null)))
        {
            check();
            if (!pages.Contains(page.Name, StringComparer.Ordinal))
            { continue; }
            yield return page;
            foreach (Placement obj in EplanNativePreconditions.Valid(page.AllPlacements))
            { check(); yield return obj; }
        }
        foreach (Connection connection in EplanNativePreconditions.Valid(finder.GetConnections(null)))
        {
            check();
            if (pages.Contains(EplanConnectionReader.PageOf(connection)?.Name, StringComparer.Ordinal))
            { yield return connection; }
        }
    }

    public DrawingLocator Refresh(string project, DrawingLocator locator)
    {
        MainThread.Assert();
        // A55: reacquire after transaction disposal; even an unchanged native handle must not be reused.
        Project nativeProject = EplanProjects.ByPath(project);
        using var finder = new DMObjectsFinder(nativeProject);
        var watch = Stopwatch.StartNew();
        void Check()
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            if (watch.Elapsed.TotalSeconds > 20)
            { throw new BridgeErrorException(ErrorReason.OutcomeUnknown, "Created object readback exceeded its budget; inspect EPLAN before retrying."); }
        }
        foreach (StorableObject obj in LifecycleCandidates(finder, nativeProject, locator.Page.Length == 0 ? null : [locator.Page], Check)
            .Where(o => o.ObjectIdentifier == locator.Oid && ObjectKindDispatch.Kind(o) is not null))
        {
            DrawingLocator fresh = Locator(project, obj);
            if (fresh.Type == locator.Type)
            { return fresh; }
        }
        throw new BridgeErrorException(ErrorReason.OutcomeUnknown, "The created object could not be found after commit; inspect EPLAN before retrying.");
    }
}
