using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanApiTryDispatcher
{
    private static string[] EntryShape(ApiTryArgs args, JObject p) => ApiTryCatalog.Shape(args.Group, p.Properties().Select(v => v.Name))!;
    private void CheckReceiver(ApiTryArgs args, JObject p, bool write, List<string> descriptions)
    {
        string[] shape = EntryShape(args, p);
        if (!ApiTryCatalog.Static(shape[1]) && !ApiTryCatalog.Factory(shape[1]))
        {
            if (p.TryGetValue("via", out JToken? via))
            { Resolve(args.Project, p["target"]!, ApiTryCatalog.ViaKinds(shape[0], (string)via!)); }
            else
            { ResolveObject(args, p["target"]!, (ApiTryCatalog.Settings(args.Group) && p["target"]?["handle"] is null || ApiTryCatalog.Service(args.Group)) ? "Eplan.EplApi.DataModel.Project" : shape[0], write, descriptions); }
        }
    }
    private sealed class EntryScope : IDisposable
    {
        private readonly List<IDisposable> _owned = [];
        internal void Own(IDisposable value) { if (!_owned.Any(o => ReferenceEquals(o, value))) { _owned.Add(value); } }
        public void Dispose() { for (int i = _owned.Count - 1; i >= 0; i--) { _owned[i].Dispose(); } }
    }
    private (object? Value, Func<string?> Owner, bool ReadOnly) Receiver(ApiTryArgs args, JObject p, EntryScope scope)
    {
        string[] shape = EntryShape(args, p);
        if (ApiTryCatalog.Static(shape[1]) || ApiTryCatalog.Factory(shape[1]))
        { return (null, () => args.Project, false); }
        if (!p.TryGetValue("via", out JToken? via))
        { return ResolveObject(args, p["target"]!, (ApiTryCatalog.Settings(args.Group) && p["target"]?["handle"] is null || ApiTryCatalog.Service(args.Group)) ? "Eplan.EplApi.DataModel.Project" : shape[0], ApiTryCatalog.MethodAccess(args.Group) == "write" || p.ContainsKey("value"), []); }
        StorableObject parent = Resolve(args.Project, p["target"]!, ApiTryCatalog.ViaKinds(shape[0], (string)via!));
        object value = ApiTryEntryDispatch.Via(shape[0], (string)via!, parent, p, scope.Own)
            ?? throw new BridgeErrorException(ErrorReason.PlanStale, "The property entry is absent; refresh the parent locator.");
        if (value is StorableObject obj && (obj is Project project ? project : obj.Project) is { } owner && ProjectResolver.SamePath(args.Project, owner.ProjectFullName))
        { return (value, () => NativeOwner(value), false); }
        if (value is StorableObject)
        { throw new BridgeErrorException(ErrorReason.PolicyDenied, "The property entry belongs to a different project or has no project."); }
        // Project-member helpers are released before the transaction leaves this request (A39/A60).
        return (value, () => NativeOwner(parent), SettingsHandle(shape[0]));
    }
    private string? ReadEntry(ApiTryArgs args)
    {
        JObject p = FileParameters(args);
        p.Remove("value");
        using var entry = new EntryScope();
        var target = Receiver(args, p, entry);
        return Convert.ToString(InvokeHandleMember(args, p, target, read: true), System.Globalization.CultureInfo.InvariantCulture);
    }
    private static object? InvokeEntry(ApiTryArgs args, JObject p, object? target, bool method, Func<JToken, string, object> resolve)
    {
        string signature = EntryShape(args, p)[1];
        if (ApiTryCatalog.Settings(args.Group) && !p.ContainsKey("via") && p["target"]?["handle"] is null)
        { return ApiTryDispatch.Invoke(signature, (Project)target!, p, (token, type) => Resolve(args.Project, token, ApiTryCatalog.Kinds(type))); }
        if (ApiTryCatalog.Entry(signature))
        { return ApiTryEntryDispatch.Invoke(signature, target, p, resolve); }
        if (!method && p.ContainsKey("value"))
        { ApiTryDispatch.Write(args.Group, (StorableObject)target!, p); }
        return method ? ApiTryDispatch.Invoke(signature, (StorableObject)target!, p, resolve) : ApiTryDispatch.Read(args.Group, (StorableObject)target!, p);
    }
}
