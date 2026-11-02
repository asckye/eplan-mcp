using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanApiTryDispatcher
{
    private object? InvokeHandleMember(ApiTryArgs args, JObject p, (object? Value, Func<string?> Owner, bool ReadOnly) target, bool read = false)
    {
        string[] shape = ApiTryCatalog.Shape(args.Group, p.Properties().Select(v => v.Name))!;
        var descriptions = new List<string>();
        bool write = !read && (ApiTryCatalog.MethodAccess(args.Group) == "write" || p.ContainsKey("value"));
        object Resolver(JToken token, string type) => ResolveObject(args, token, type, write, descriptions).Value;
        // A67: project-anchored settings use the text adapter even when a native handle signature also exists.
        bool settings = ApiTryCatalog.Settings(args.Group) && !p.ContainsKey("via") && p["target"]?["handle"] is null;
        bool extra = !settings && (ApiTryCatalog.HandleShape(args.Group) is not null || ApiTryCatalog.HandleMethodShape(args.Group, p.Properties().Select(v => v.Name)) is not null);
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
}
