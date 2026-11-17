using System.Collections;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanApiTryDispatcher
{
    private static IEnumerable<(JToken Token, string Type)> ArgumentObjects(string[] shape, JObject parameters)
    {
        foreach (string name in shape.Skip(2))
        {
            string[]? valueShape = ApiTryCatalog.ParameterShape(shape[1], name);
            if (valueShape is null)
            { continue; }
            string type = valueShape[valueShape[0] == "array" ? 1 : 0];
            if (ApiTryCatalog.Kinds(type).Length == 0 && !ApiTryCatalog.HandleType(type) && type != "Eplan.EplApi.Base.PointD")
            { continue; }
            IEnumerable<JToken> tokens = valueShape[0] == "array" ? parameters[name]!.Children() : [parameters[name]!];
            foreach (JToken token in tokens)
            { yield return (token, type); }
        }
    }
    private static object? Detach(string project, object? result, bool nested = false, Func<object, object>? native = null, Func<object, bool>? nativeType = null)
    {
        OperationContext.Current.ThrowIfCancellationRequested();
        if (result is double number && (double.IsNaN(number) || double.IsInfinity(number)) || result is PointD coordinates && (double.IsNaN(coordinates.X) || double.IsInfinity(coordinates.X) || double.IsNaN(coordinates.Y) || double.IsInfinity(coordinates.Y)))
        { throw new InvalidOperationException("Returned coordinates and numbers must be finite."); }
        if (result is StorableObject obj)
        { return native is null ? Locator(project, obj) : native(obj); }
        if (result is PointD point)
        { return new JObject { ["x"] = point.X, ["y"] = point.Y }; }
        if (result is not null && native is not null && nativeType?.Invoke(result) == true)
        { return native(result); }
        if (result is IDictionary<string, object?> named)
        { return new JObject(named.Select(p => new JProperty(p.Key, Token(Detach(project, p.Value, true, native, nativeType))))); }
        if (result is IEnumerable sequence && result is not string)
        {
            var items = new JArray();
            foreach (object? item in sequence)
            {
                if (items.Count == 200)
                { throw new InvalidOperationException("Trial results exceed 200 elements; narrow the input scope."); }
                items.Add(Token(Detach(project, item, true, native, nativeType)));
            }
            return items;
        }
        if (result is DateTime time)
        { return time.ToString("o", System.Globalization.CultureInfo.InvariantCulture); }
        if (result is float single && (float.IsNaN(single) || float.IsInfinity(single)))
        { throw new InvalidOperationException("Returned coordinates and numbers must be finite."); }
        // Result-only scalars of the generator (ApiTryGenerator.ResultScalar) are plain values, never native handles.
        if (result is not null && result is not (string or bool or int or long or double or Enum or short or ushort or uint or byte or sbyte or float) && native is not null)
        { return native(result); }
        return result is null ? null : nested && result is not Enum ? result : Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture);
    }
    private static JToken Token(object? value) => value is null ? JValue.CreateNull() : value is JToken token ? token : value is DrawingLocator locator
        ? new JObject { ["project"] = locator.Project, ["page"] = locator.Page, ["type"] = locator.Type, ["oid"] = locator.Oid, ["digest"] = locator.Digest } : value is ApiObjectHandle handle
        ? new JObject { ["handle"] = handle.Handle, ["type"] = handle.Type } : JToken.FromObject(value);
}
