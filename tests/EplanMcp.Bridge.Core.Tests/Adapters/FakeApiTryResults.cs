using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanApiTryDispatcher
{
    internal int ReturnedHandles { get; private set; }
    internal object? InvokeResult(ApiTryArgs args) => InvokeHandleMember(args, JObject.Parse(args.Parameters), (new object(), () => args.Project, false));
    private static (object Value, Func<string?> Owner, bool ReadOnly) ResolveObject(ApiTryArgs args, JToken token, string type, bool write, List<string> descriptions) => throw new NotSupportedException();
    private ApiObjectHandle NativeResult(ApiTryArgs args, object value, (object? Value, Func<string?> Owner, bool ReadOnly) target, string? declared = null)
    {
        ReturnedHandles++;
        return new ApiObjectHandle { Handle = "h:" + new string('b', 32), Type = ApiTryHandleDispatch.Type(value) ?? declared! };
    }
    private static object? InvokeEntry(ApiTryArgs args, JObject parameters, object? target, bool method, Func<JToken, string, object> resolve) => args.Group switch
    {
        "O:Eplan.EplApi.DataModel.Project.Settings" => "PROJECT",
        "O:Eplan.EplApi.DataModel.Project.DeviceTagSettings.AllowedLetters" => true,
        "O:Eplan.EplApi.Base.SettingNode.GetListOfNodes" => "{\"items\":[\"A\"],\"total\":1,\"nextOffset\":null}",
        "O:Eplan.EplApi.DataModel.ProjectSettings.GetStringSetting" => "setting text",
        _ => throw new NotSupportedException(),
    };
}

internal static class ApiTryHandleDispatch
{
    private sealed record NativeValue(string Type);
    internal static object Invoke(string signature, object target, JObject parameters, Func<JToken, string, object> resolve) => signature switch
    {
        "Eplan.EplApi.DataModel.Project.Settings() : Eplan.EplApi.DataModel.ProjectSettings" => new NativeValue("Eplan.EplApi.DataModel.ProjectSettings"),
        "Eplan.EplApi.DataModel.Connection.StartPin() : Eplan.EplApi.DataModel.Pin" => new NativeValue("Eplan.EplApi.DataModel.Pin"),
        "Eplan.EplApi.DataModel.Pin.Connections() : Eplan.EplApi.DataModel.Connection[]" => new[] { new NativeValue("Eplan.EplApi.DataModel.Connection") },
        _ => throw new NotSupportedException(),
    };
    internal static object? Elements(string signature, object? value) => signature.Contains(".Pin.Connections(", StringComparison.Ordinal) ? new ApiTryNativeElements(value, false, "Eplan.EplApi.DataModel.Connection") : value;
    internal static string? Type(object value) => (value as NativeValue)?.Type;
    internal static string? ReturnType(string signature) => null;
    internal static bool Compatible(object value, string type) => Type(value) == type;
}
