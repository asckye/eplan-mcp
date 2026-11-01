using System.Diagnostics;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Eplan.Adapters;
/// <summary>A12/A36/A39 borrowed identities; no EPLAN object crosses a request boundary.</summary>
internal sealed partial class EplanApiTryDispatcher : IApiTryDispatcher
{
    public void CheckTarget(ApiTryArgs args)
    {
        JObject parameters = ApiTryPlanOperation.Parameters(args.Parameters);
        CheckReceiver(args, parameters);
        string[] shape = ApiTryCatalog.Shape(args.Group, parameters.Properties().Select(p => p.Name))!;
        foreach (string name in shape.Skip(2).Where(n => parameters[n] is JObject && ApiTryCatalog.ParameterShape(shape[1], n)?[0] != "Eplan.EplApi.Base.PointD"))
        { Resolve(args.Project, parameters[name]!, ApiTryCatalog.Kinds(ApiTryCatalog.ParameterShape(shape[1], name)![0])); }
    }
    private static StorableObject Resolve(string project, JToken token, string[] kinds)
    {
        MainThread.Assert();
        DrawingLocator locator = token.ToObject<DrawingLocator>()!;
        if (!ProjectResolver.SamePath(project, locator.Project))
        { throw new BridgeErrorException(ErrorReason.PolicyDenied, "Every locator must belong to the target scratch project."); }
        Project nativeProject = EplanProjects.ByPath(project);
        using var finder = new DMObjectsFinder(nativeProject);
        foreach (string kind in kinds)
        {
            IEnumerable<StorableObject> candidates = EplanObjectReader.Candidates(finder, kind, () => OperationContext.Current.ThrowIfCancellationRequested(), locator.Page, nativeProject);
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
    public string? Read(ApiTryArgs args) => ReadEntry(args);
    public object? Invoke(ApiTryArgs args)
    {
        using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
        JObject parameters = FileParameters(args);
        using var entry = new EntryScope();
        object? target = Receiver(args, parameters, entry);
        bool method = ApiTryCatalog.MethodAccess(args.Group) is not null;
        object? result = InvokeEntry(args, parameters, target, method);
        object? value = result is StorableObject obj ? Locator(args.Project, obj) : result is null ? null : Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture);
        using var process = Process.GetCurrentProcess();
        if (process.MainWindowHandle != IntPtr.Zero && !Interop.NativeMethods.IsWindowEnabled(process.MainWindowHandle))
        { throw new BridgeErrorException(ErrorReason.EplanBusy, "A modal dialog appeared during the unverified call."); }
        return value;
    }
    private static DrawingLocator Locator(string project, StorableObject obj)
    {
        Project? owner = obj as Project ?? obj.Project;
        if (owner is null || !ProjectResolver.SamePath(project, owner.ProjectFullName))
        { throw new BridgeErrorException(ErrorReason.PolicyDenied, "The returned object does not belong to the target scratch project."); }
        string kind = ObjectReadTypes.Types.Where(ObjectReadTypes.Supported).OrderBy(k => k == "PLCTerminal" ? 1 : 0).Select(k => k.ToLowerInvariant()).FirstOrDefault(k => EplanObjectReader.Matches(obj, k))
            ?? throw new BridgeErrorException(ErrorReason.CapabilityMissing, "The returned object has no supported discovery locator.");
        SnapshotObject identity = EplanObjectReader.Describe(obj, kind);
        return new() { Project = project, Page = obj is Page page ? page.Name : identity.Page ?? string.Empty, Type = identity.Type, Oid = identity.Oid, Digest = SnapshotCanonical.Sha256(Newtonsoft.Json.JsonConvert.SerializeObject(identity)) };
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
