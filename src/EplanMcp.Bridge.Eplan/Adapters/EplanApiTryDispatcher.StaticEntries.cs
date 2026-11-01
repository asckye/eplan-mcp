using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanApiTryDispatcher
{
    private static string[] EntryShape(ApiTryArgs args, JObject p) => ApiTryCatalog.Shape(args.Group, p.Properties().Select(v => v.Name))!;
    private static void CheckReceiver(ApiTryArgs args, JObject p)
    {
        string[] shape = EntryShape(args, p);
        if (!ApiTryCatalog.Static(shape[1]))
        { Resolve(args.Project, p["target"]!, p.TryGetValue("via", out JToken? via) ? ApiTryCatalog.ViaKinds(shape[0], (string)via!) : ApiTryCatalog.TargetKinds(args.Group)); }
    }
    private sealed class EntryScope : IDisposable
    {
        private readonly List<IDisposable> _owned = [];
        internal void Own(IDisposable value) { if (!_owned.Any(o => ReferenceEquals(o, value))) { _owned.Add(value); } }
        public void Dispose() { for (int i = _owned.Count - 1; i >= 0; i--) { _owned[i].Dispose(); } }
    }
    private static object? Receiver(ApiTryArgs args, JObject p, EntryScope scope)
    {
        string[] shape = EntryShape(args, p);
        if (ApiTryCatalog.Static(shape[1]))
        { return null; }
        if (!p.TryGetValue("via", out JToken? via))
        { return Resolve(args.Project, p["target"]!, ApiTryCatalog.TargetKinds(args.Group)); }
        StorableObject parent = Resolve(args.Project, p["target"]!, ApiTryCatalog.ViaKinds(shape[0], (string)via!));
        object value = ApiTryEntryDispatch.Via(shape[0], (string)via!, parent, p, scope.Own)
            ?? throw new BridgeErrorException(ErrorReason.PlanStale, "The property entry is absent; refresh the parent locator.");
        if (value is StorableObject obj && (obj is Project project ? project : obj.Project) is { } owner && ProjectResolver.SamePath(args.Project, owner.ProjectFullName))
        { return value; }
        if (value is StorableObject)
        { throw new BridgeErrorException(ErrorReason.PolicyDenied, "The property entry belongs to a different project or has no project."); }
        return value; // Project-member helpers are released before the transaction leaves this request (A39/A60).
    }
    private static string? ReadEntry(ApiTryArgs args)
    {
        JObject p = ApiTryPlanOperation.Parameters(args.Parameters);
        using var entry = new EntryScope();
        object? target = Receiver(args, p, entry);
        if (!ApiTryCatalog.Entry(EntryShape(args, p)[1]))
        { return ApiTryDispatch.Read(args.Group, (StorableObject)target!, p); }
        p.Remove("value");
        return Convert.ToString(ApiTryEntryDispatch.Invoke(EntryShape(args, p)[1], target, p, (token, type) => Resolve(args.Project, token, ApiTryCatalog.Kinds(type))), System.Globalization.CultureInfo.InvariantCulture);
    }
    private static object? InvokeEntry(ApiTryArgs args, JObject p, object? target, bool method)
    {
        string signature = EntryShape(args, p)[1];
        if (ApiTryCatalog.Settings(args.Group) && !p.ContainsKey("via"))
        { return ApiTryDispatch.Invoke(signature, (Project)target!, p, (token, type) => Resolve(args.Project, token, ApiTryCatalog.Kinds(type))); }
        if (ApiTryCatalog.Entry(signature))
        { return ApiTryEntryDispatch.Invoke(signature, target, p, (token, type) => Resolve(args.Project, token, ApiTryCatalog.Kinds(type))); }
        if (!method && p.ContainsKey("value"))
        { ApiTryDispatch.Write(args.Group, (StorableObject)target!, p); }
        return method ? ApiTryDispatch.Invoke(signature, (StorableObject)target!, p, (token, type) => Resolve(args.Project, token, ApiTryCatalog.Kinds(type))) : ApiTryDispatch.Read(args.Group, (StorableObject)target!, p);
    }
}
