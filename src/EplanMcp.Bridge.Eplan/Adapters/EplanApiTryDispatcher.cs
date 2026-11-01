using System.Diagnostics;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Eplan.Adapters;
/// <summary>A12/A36/A39 borrowed identities; ADR-0033 handles remain inside the add-in.</summary>
internal sealed partial class EplanApiTryDispatcher : IApiTryDispatcher
{
    public void CheckTarget(ApiTryArgs args)
    {
        CheckHandleTargets(args);
        if (ApiTryCatalog.Settings(args.Group))
        { CheckSettings(args); }
    }
    private static StorableObject Resolve(string project, JToken token, string[] kinds)
    {
        MainThread.Assert();
        DrawingLocator locator = token.ToObject<DrawingLocator>()!;
        if (!ProjectResolver.SamePath(project, locator.Project))
        { throw new BridgeErrorException(ErrorReason.PolicyDenied, "Every locator must belong to the target scratch project."); }
        Project nativeProject = EplanProjects.ByPath(project);
        using var finder = new DMObjectsFinder(nativeProject);
        var watch = Stopwatch.StartNew();
        void Check()
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            if (watch.Elapsed.TotalSeconds > 20)
            { throw new BridgeErrorException(ErrorReason.Timeout, "Trial target resolution exceeded 20 seconds; refresh the locator or reduce the scratch project."); }
        }
        // A32/A60: a placed locator records its page. Avoid rediscovering that page's objects for every via kind.
        IEnumerable<StorableObject> Nearby()
        {
            yield return nativeProject;
            if (locator.Page.Length == 0)
            { yield break; }
            foreach (Page page in finder.GetPages(null))
            {
                Check();
                if (page.Name != locator.Page)
                { continue; }
                yield return page;
                foreach (Placement placed in page.AllPlacements)
                { Check(); yield return placed; }
            }
        }
        return ApiTryTargets.Resolve(locator, kinds, Nearby().Where(obj => obj.ObjectIdentifier == locator.Oid),
            kind => EplanObjectReader.Candidates(finder, kind, Check, locator.Page, nativeProject),
            (obj, kind) =>
            {
                Check();
                if (obj.ObjectIdentifier != locator.Oid || SnapshotCanonical.TypeOf(obj.ToStringIdentifier()) != locator.Type || !EplanObjectReader.Matches(obj, kind))
                { return null; }
                SnapshotObject identity = EplanObjectReader.Describe(obj, kind);
                return (identity, obj is Page page ? page.Name : identity.Page ?? string.Empty);
            });
    }
    public string? Read(ApiTryArgs args) => ReadEntry(args);
    public object? Invoke(ApiTryArgs args)
    {
        using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
        JObject parameters = FileParameters(args);
        using var entry = new EntryScope();
        var target = Receiver(args, parameters, entry);
        object? value = InvokeHandleMember(args, parameters, target);
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
