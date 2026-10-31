using System.Diagnostics;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Eplan.Adapters;
/// <summary>A12/A36/A39 borrowed identities; no EPLAN object crosses a request boundary.</summary>
internal sealed class EplanApiTryDispatcher : IApiTryDispatcher
{
    public void CheckTarget(ApiTryArgs args) => Resolve(args);
    private static StorableObject Resolve(ApiTryArgs args)
    {
        MainThread.Assert();
        DrawingLocator locator = ApiTryPlanOperation.Parameters(args.Parameters)["target"]!.ToObject<DrawingLocator>()!;
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(args.Project));
        IEnumerable<StorableObject> candidates = ApiTryCatalog.Shape(args.Group)![0] switch
        {
            "Eplan.EplApi.DataModel.Page" => finder.GetPages(null),
            "Eplan.EplApi.DataModel.Function" => EplanPropertyReadAdapter.Functions(finder, () => OperationContext.Current.ThrowIfCancellationRequested()),
            _ => finder.GetConnections(null),
        };
        StorableObject? found = candidates.FirstOrDefault(o => o.ObjectIdentifier == locator.Oid && SnapshotCanonical.TypeOf(o.ToStringIdentifier()) == locator.Type);
        SnapshotObject? identity = found is null ? null : EplanPropertyReadAdapter.Describe(found);
        if (identity is null || identity.Page != locator.Page && found is not Page || found is Page page && page.Name != locator.Page
            || SnapshotCanonical.Sha256(Newtonsoft.Json.JsonConvert.SerializeObject(identity)) != locator.Digest)
        { throw new BridgeErrorException(ErrorReason.PlanStale, "Object identity changed; get a fresh locator from eplan_object_find."); }
        return found!;
    }
    public string? Invoke(ApiTryArgs args)
    {
        using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
        string? value = ApiTryDispatch.Read(args.Group, Resolve(args), ApiTryPlanOperation.Parameters(args.Parameters));
        using var process = Process.GetCurrentProcess();
        if (process.MainWindowHandle != IntPtr.Zero && !Interop.NativeMethods.IsWindowEnabled(process.MainWindowHandle))
        { throw new BridgeErrorException(ErrorReason.EplanBusy, "A modal dialog appeared during the unverified call."); }
        return value;
    }
    public Dictionary<string, string> Inspect(string project)
    {
        var watch = Stopwatch.StartNew();
        Dictionary<string, string> state = new EplanActionRunner().Inspect("api.try", project, new Dictionary<string, string>()).Before;
        using ISnapshotSource source = new EplanSnapshotReader().Open(project);
        foreach (KeyValuePair<long, string> page in source.Pages.OrderBy(p => p.Key))
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            if (watch.Elapsed.TotalSeconds > 20) { throw new BridgeErrorException(ErrorReason.Timeout, "Trial snapshot exceeded 20 seconds; reduce the scratch project."); }
            IReadOnlyList<SnapshotSourceObject> objects = source.ReadPage(page.Key, SnapshotProfiles.StandardName);
            state["snapshot:" + page.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)] = SnapshotCanonical.Page(SnapshotProfiles.StandardName, objects[0], objects.Skip(1), SnapshotCanonical.Registered(SnapshotProfiles.StandardName)!).Hash;
        }
        return state;
    }
}
