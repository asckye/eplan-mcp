using System.Diagnostics;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanApiTryDispatcher : IDisposable
{
    private readonly ApiTryHandleTable _handles = new(() => DateTime.UtcNow);
    private readonly global::Eplan.EplApi.ApplicationFramework.EventHandler _handleClose = new("Eplan.EplApi.OnPreCloseProject");
    private readonly global::Eplan.EplApi.ApplicationFramework.EventHandler _handleUserClose = new("Eplan.EplApi.OnUserPreCloseProject");
    private IEnumerable<global::Eplan.EplApi.ApplicationFramework.EventHandler> HandleEvents => [_handleClose, _handleUserClose];
    private readonly global::EplanMcp.Bridge.Core.Diagnostics.IBridgeLog _log;
    public EplanApiTryDispatcher(global::EplanMcp.Bridge.Core.Diagnostics.IBridgeLog log)
    {
        _log = log;
        foreach (global::Eplan.EplApi.ApplicationFramework.EventHandler handler in HandleEvents)
        { handler.EplanNameEvent += (_, _) => { try { _handles.Clear(); } catch (Exception ex) { Hosting.StartupFailureLog.Write(ex); } }; }
    }
    public void Dispose() { _handles.Clear(); foreach (global::Eplan.EplApi.ApplicationFramework.EventHandler handler in HandleEvents) { handler.Dispose(); } }
    private static bool HostAlive(string session)
    {
        string[] parts = session.Split(':');
        try
        { using var host = Process.GetProcessById(int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture)); return !host.HasExited && host.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) == parts[1]; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or FormatException or IndexOutOfRangeException) { return false; }
    }
    private static string? NativeOwner(object value) => (value is StorableObject ? value : ApiTryHandleDispatch.Owner(value)) is StorableObject { IsValid: true } obj ? (obj as Project ?? obj.Project)?.ProjectFullName : null;
    private static bool SettingsHandle(string type) => type.IndexOf("Settings", StringComparison.OrdinalIgnoreCase) >= 0 || type.IndexOf("Scheme", StringComparison.OrdinalIgnoreCase) >= 0 || type.IndexOf("ProjectSections", StringComparison.OrdinalIgnoreCase) >= 0;
    private static bool Scratch(ApiTryArgs args, string path)
    {
        if (args.ScratchPath.Length == 0 || !ProjectResolver.SamePath(args.ScratchPath, path) || !ProjectPaths.IsPlainFullPath(path))
        { return false; }
        return ApiTryScratchPaths.IsApproved(args.ScratchPath, path, EplanProjects.ByPath(path).ProjectLinkFilePath, System.IO.File.GetAttributes);
    }
    private (object Value, Func<string?> Owner, bool ReadOnly) ResolveObject(ApiTryArgs args, JToken token, string type, bool write, List<string> descriptions)
    {
        if (type == "Eplan.EplApi.Base.PointD" && token["x"] is not null)
        { return (new System.Runtime.CompilerServices.StrongBox<global::Eplan.EplApi.Base.PointD>(new(token["x"]!.Value<double>(), token["y"]!.Value<double>())), () => args.Project, false); }
        if (token["handle"] is not null)
        {
            var result = _handles.Resolve(args.HostSession, token.ToObject<ApiObjectHandle>()!, type, args.Project, ApiTryHandleDispatch.Compatible, path => Scratch(args, path), out string description, write);
            descriptions.Add(description);
            return result;
        }
        StorableObject obj = Resolve(args.Project, token, ApiTryCatalog.Kinds(type));
        return (obj, () => NativeOwner(obj), false);
    }
    private void CheckHandleTargets(ApiTryArgs args)
    {
        MainThread.Assert();
        _handles.Sweep(HostAlive);
        JObject p = ApiTryPlanOperation.Parameters(args.Parameters);
        string[] shape = ApiTryCatalog.Shape(args.Group, p.Properties().Select(v => v.Name))!;
        bool write = ApiTryCatalog.MethodAccess(args.Group) == "write" || p.ContainsKey("value");
        var descriptions = new List<string>();
        CheckReceiver(args, p, write, descriptions);
        foreach ((JToken token, string type) in ArgumentObjects(shape, p))
        { ResolveObject(args, token, type, write, descriptions); }
        args.HandleSources = string.Join("; ", descriptions);
    }
    private object? InvokeHandleMember(ApiTryArgs args, JObject p, (object? Value, Func<string?> Owner, bool ReadOnly) target, bool read = false)
    {
        string[] shape = ApiTryCatalog.Shape(args.Group, p.Properties().Select(v => v.Name))!;
        var descriptions = new List<string>();
        bool write = !read && (ApiTryCatalog.MethodAccess(args.Group) == "write" || p.ContainsKey("value"));
        object Resolver(JToken token, string type) => ResolveObject(args, token, type, write, descriptions).Value;
        bool extra = ApiTryCatalog.HandleShape(args.Group) is not null || ApiTryCatalog.HandleMethodShape(args.Group, p.Properties().Select(v => v.Name)) is not null;
        object? result;
        if (extra)
        { result = ApiTryHandleDispatch.Invoke(shape[1], target.Value!, p, Resolver); }
        else
        { result = InvokeEntry(args, p, target.Value, ApiTryCatalog.MethodAccess(args.Group) is not null, Resolver); }
        if (result is null || read)
        { return result; }
        result = ApiTryHandleDispatch.Elements(shape[1], result);
        if (result is ApiTryNativeElements elements)
        { return CaptureElements(args, elements, target, shape[1]); }
        return Detach(args.Project, result, native: value => NativeResult(args, value, target, ReferenceEquals(value, result) ? ChainResultType(value, shape[1]) : null), nativeType: value => ApiTryHandleDispatch.Type(value) is not null);
    }
    private object NativeResult(ApiTryArgs args, object result, (object? Value, Func<string?> Owner, bool ReadOnly) target, string? declared = null)
    {
        if (result is StorableObject obj && ObjectReadTypes.Types.Where(ObjectReadTypes.Supported).Any(k => EplanObjectReader.Matches(obj, k.ToLowerInvariant())))
        { return Locator(args.Project, obj); }
        if ((ApiTryHandleDispatch.Type(result) ?? declared) is not { } nativeType)
        { throw new BridgeErrorException(ErrorReason.CapabilityMissing, "The returned native type has no compiled handle adapter."); }
        object borrowed = ApiTryHandleDispatch.Box(result);
        if (!ApiTryHandleDispatch.Compatible(borrowed, nativeType))
        { throw new BridgeErrorException(ErrorReason.CapabilityMissing, "The returned object does not match its compiled type."); }
        Func<string?> check = ReferenceEquals(ApiTryHandleDispatch.Owner(result), result) && result is not StorableObject ? target.Owner : () => NativeOwner(result);
        string? project = check();
        if (project is null || !ProjectResolver.SamePath(args.Project, project) || !Scratch(args, project))
        { throw new BridgeErrorException(ErrorReason.PolicyDenied, "The returned object must belong to the approved scratch project."); }
        return _handles.Add(args.HostSession, borrowed, nativeType, args.Group, check, target.ReadOnly || ChainReadOnly(nativeType));
    }
}
