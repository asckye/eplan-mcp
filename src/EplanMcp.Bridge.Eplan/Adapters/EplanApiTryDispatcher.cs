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
        foreach (string kind in ApiTryCatalog.TargetKinds(args.Group))
        {
            IEnumerable<StorableObject> candidates = EplanObjectReader.Candidates(finder, kind, () => OperationContext.Current.ThrowIfCancellationRequested(), locator.Page);
            foreach (StorableObject found in candidates.Where(o => o.ObjectIdentifier == locator.Oid && EplanObjectReader.Matches(o, kind)))
            {
                SnapshotObject identity = EplanObjectReader.Describe(found, kind);
                if (identity.Type == locator.Type && (found is Page page ? page.Name : identity.Page ?? string.Empty) == locator.Page
                    && SnapshotCanonical.Sha256(Newtonsoft.Json.JsonConvert.SerializeObject(identity)) == locator.Digest)
                { return found; }
            }
        }
        throw new BridgeErrorException(ErrorReason.PlanStale, "Object identity changed; get a fresh locator from eplan_object_find.");
    }
    public string? Read(ApiTryArgs args) => ApiTryDispatch.Read(args.Group, Resolve(args), ApiTryPlanOperation.Parameters(args.Parameters));
    public string? Invoke(ApiTryArgs args)
    {
        using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
        StorableObject target = Resolve(args);
        Newtonsoft.Json.Linq.JObject parameters = ApiTryPlanOperation.Parameters(args.Parameters);
        if (parameters.ContainsKey("value"))
        { ApiTryDispatch.Write(args.Group, target, parameters); }
        string? value = ApiTryDispatch.Read(args.Group, target, parameters);
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
            if (watch.Elapsed.TotalSeconds > 20)
            { throw new BridgeErrorException(ErrorReason.Timeout, "Trial snapshot exceeded 20 seconds; reduce the scratch project."); }
            IReadOnlyList<SnapshotSourceObject> objects = source.ReadPage(page.Key, SnapshotProfiles.StandardName);
            state["snapshot:" + page.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)] = SnapshotCanonical.Page(SnapshotProfiles.StandardName, objects[0], objects.Skip(1), SnapshotCanonical.Registered(SnapshotProfiles.StandardName)!).Hash;
        }
        return state;
    }
}
