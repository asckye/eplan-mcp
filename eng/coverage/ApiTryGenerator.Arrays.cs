using EplanMcp.Host.Tools;
namespace EplanMcp.Coverage;

internal static partial class ApiTryGenerator
{
    internal static string? Element(string type)
    {
        if (type.EndsWith("[]", StringComparison.Ordinal))
        { return type[..^2]; }
        string[] containers = ["IEnumerable", "List", "IList", "ICollection"];
        return containers.Any(c => type.StartsWith("System.Collections.Generic." + c + "{", StringComparison.Ordinal)) && type.EndsWith('}')
            ? type[(type.IndexOf('{', StringComparison.Ordinal) + 1)..^1] : null;
    }
    private static string ValueType(ApiParameter p) => p.Type.TrimEnd('@');
    private static bool Out(ApiParameter p) => p.Direction == "out";
    private static IEnumerable<ApiParameter> Inputs(ApiDetail member) => member.Parameters!.Where(p => !Out(p));
    private static IEnumerable<string> ProducedTypes(ApiDetail member) => new[] { member.ReturnType }.Concat(member.Parameters!.Where(Out).Select(ValueType))
        .Where(t => t is not null).Select(t => Element(t!) ?? t!).Distinct(StringComparer.Ordinal);
    private static bool Supported(string? type, IReadOnlyDictionary<string, string[]>? enums, IReadOnlyDictionary<string, string[]>? kinds) => type is not null
        && (Scalar(type, enums) || type == "Eplan.EplApi.Base.PointD" || ObjectType(type, kinds));
    internal static bool PropertyReturn(string? type, IReadOnlyDictionary<string, string[]>? enums, IReadOnlyDictionary<string, string[]>? kinds) => Scalar(type, enums)
        || type == "Eplan.EplApi.Base.PointD" || type is not null && Element(type) is { } element && Supported(element, enums, kinds);
    private static bool PropertyWritable(ApiDetail member, IReadOnlyDictionary<string, string[]>? enums) => Writable(member) && Scalar(member.ReturnType, enums) && member.ReturnType != "System.Int64";
    internal static string? ShapeExclusion(ApiDetail member, IReadOnlyDictionary<string, string[]>? enums, IReadOnlyDictionary<string, string[]>? kinds)
    {
        // A70: the catalog omits modreq(IsByValue); Roslyn cannot bind this native C++ collection overload.
        if (member.Id == "M:Eplan.EplApi.DataModel.E3D.Placement3D.CopyTo(Eplan.EplApi.DataModel.E3D.Placement3D,System.Collections.Generic.List{Eplan.EplApi.DataModel.E3D.Placement3D})")
        { return "unsupported_required_modifier"; }
        bool Value(string? t) => Supported(t, enums, kinds) || t is not null && Element(t) is { } e && Supported(e, enums, kinds);
        foreach (ApiParameter p in member.Parameters!)
        {
            if (p.Direction is not (null or "out" or "ref") || (p.Type.EndsWith('@') || p.Direction == "ref") && !Out(p) && !Scalar(ValueType(p), enums) && ValueType(p) != "Eplan.EplApi.Base.PointD")
            { return "ref_out"; }
            if (Value(ValueType(p)))
            { continue; }
            if (p.Type.Contains('[', StringComparison.Ordinal))
            { return "array_or_params"; }
            if (p.Type.StartsWith("System.Action", StringComparison.Ordinal) || p.Type.StartsWith("System.Func", StringComparison.Ordinal))
            { return "delegate_parameter"; }
            return p.Type.Contains('{', StringComparison.Ordinal) || p.Type.Contains('`', StringComparison.Ordinal) || p.Type.StartsWith("System.Collections.", StringComparison.Ordinal) ? "generic_or_collection_parameter" : "unsupported_parameter";
        }
        return member.Id.Contains('`', StringComparison.Ordinal) ? "generic_or_collection_parameter" : member.ReturnType != "System.Void" && !Value(member.ReturnType) ? "unsupported_return" : null;
    }
    private static string CollectionShape(string type, Dictionary<string, string[]> enums)
    {
        type = type.TrimEnd('@');
        string? element = Element(type);
        type = element ?? type;
        return "[" + string.Join(", ", (element is null ? Array.Empty<string>() : ["array"]).Concat(enums.TryGetValue(type, out string[]? values) ? _enumShape.Concat(values) : [type]).Select(Literal)) + "]";
    }
    private static string Native(string type) => Element(type) is { } e && !type.EndsWith("[]", StringComparison.Ordinal) ? type[..type.IndexOf('{', StringComparison.Ordinal)] + "<global::" + Native(e) + ">" : type;
    internal static string ArgumentValue(string type, string token, IReadOnlyDictionary<string, string[]> enums, IReadOnlyDictionary<string, string[]> kinds, IReadOnlySet<string>? structs = null)
    {
        if (Element(type) is { } element)
        {
            string values = "System.Linq.Enumerable.Select((JArray)" + token + "!, item => " + ArgumentValue(element, "item", enums, kinds, structs) + ")";
            return type.EndsWith("[]", StringComparison.Ordinal) ? "System.Linq.Enumerable.ToArray(" + values + ")" : "((global::" + Native(type) + ")System.Linq.Enumerable.ToList(" + values + "))";
        }
        return enums.TryGetValue(type, out string[]? names) ? "(" + token + "!.Value<string>() switch { " + string.Join(", ", names.Select(v => Literal(v) + " => global::" + type + "." + v)) + ", _ => throw new System.InvalidOperationException(\"Invalid enum value.\") })"
            : type == "Eplan.EplApi.Base.PointD" || structs?.Contains(type) == true ? "((System.Runtime.CompilerServices.StrongBox<global::" + type + ">)resolve(" + token + "!, " + Literal(type) + ")).Value"
            : ObjectType(type, kinds) ? "((global::" + type + ")resolve(" + token + "!, " + Literal(type) + "))" : token + "!.Value<" + type + ">()";
    }
    internal static string MethodCase(ApiEntry entry, ApiDetail member, IReadOnlyDictionary<string, string[]> enums, IReadOnlyDictionary<string, string[]> kinds, IReadOnlyDictionary<string, ApiTryServices.Service> services, string? receiverExpression = null, IReadOnlySet<string>? structs = null)
    {
        if (Factory(entry))
        { return FactoryDispatch(entry, member, p => ArgumentValue(p.Type, "parameters[" + Literal(ParameterName(p)) + "]", enums, kinds, structs)); }
        ApiParameter[] parameters = member.Parameters!;
        ApiParameter[] byref = parameters.Where(p => p.Type.EndsWith('@') || p.Direction is "out" or "ref").ToArray();
        bool service = services.TryGetValue(entry.Type[2..], out ApiTryServices.Service? receiver) && receiverExpression is null;
        string construct = service ? (receiver!.Disposable ? "using " : "") + "var service = new global::" + entry.Type[2..] + "(); " : "";
        string Token(ApiParameter p) => "parameters[" + Literal(ParameterName(p)) + "]";
        string Local(ApiParameter p) => "arg" + Array.IndexOf(parameters, p);
        string locals = string.Concat(byref.Select(p => "global::" + Native(ValueType(p)) + " " + Local(p) + (Out(p) ? "; " : " = " + ArgumentValue(ValueType(p), Token(p), enums, kinds, structs) + "; ")));
        string call = (receiverExpression is not null ? receiverExpression + "." + entry.Name : service ? "service." + entry.Name : Target(entry)) + "(" + string.Join(", ", parameters.Select(p => byref.Contains(p) ? (Out(p) ? "out " : "ref ") + Local(p) : ArgumentValue(p.Type, Token(p), enums, kinds, structs))) + "); ";
        string prefix = member.ReturnType == "System.Void" ? "" : byref.Length == 0 ? "return " : "var returned = ";
        string result = byref.Length == 0 ? member.ReturnType == "System.Void" ? "return null;" : "" : "return new System.Collections.Generic.Dictionary<string, object?> { [\"returnValue\"] = " + (member.ReturnType == "System.Void" ? "null" : "returned") + ", [\"outputs\"] = new System.Collections.Generic.Dictionary<string, object?> { " + string.Join(", ", byref.Select(p => "[" + Literal(p.Name) + "] = " + Local(p))) + " } };";
        return "            case " + Literal(member.Signature!) + ": { " + construct + locals + prefix + call + result + " }";
    }
}
