using System.Collections;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed class ApiTryNativeElements(object? value, bool collection, string elementType)
{
    internal object? Value { get; } = value;
    internal bool Collection { get; } = collection;
    internal string ElementType { get; } = elementType;
}
internal sealed partial class EplanApiTryDispatcher
{
    private static bool ChainReadOnly(string type) => SettingsHandle(type) || type.IndexOf("Setting", StringComparison.OrdinalIgnoreCase) >= 0;
    private static string? ChainResultType(object value, string signature)
    {
        string? known = ApiTryHandleDispatch.Type(value), declared = ApiTryHandleDispatch.ReturnType(signature);
        return known ?? (declared is not null && ApiTryHandleDispatch.Compatible(value, declared) ? declared : null);
    }
    private object? CaptureElements(ApiTryArgs args, ApiTryNativeElements elements, (object? Value, Func<string?> Owner, bool ReadOnly) target, string signature)
    {
        if (elements.Value is null)
        { return null; }
        // Detach inside bounded enumeration, just like main's collection path; do not retain a second native list.
        object? collection = elements.Collection ? NativeResult(args, elements.Value, target, ChainResultType(elements.Value, signature)) : null;
        object?[] items = elements.Value is IEnumerable source ? ApiTryHandleElements.Map(source, item => Detach(args.Project, item, nested: true,
            native: value => NativeResult(args, value, target, elements.ElementType), nativeType: value => ApiTryHandleDispatch.Type(value) is not null)) : [];
        var detached = new JArray(items.Select(Token));
        return elements.Collection ? new JObject
        {
            ["returnValue"] = Token(collection),
            ["outputs"] = new JObject { ["items"] = Token(detached) },
        } : detached;
    }
}
