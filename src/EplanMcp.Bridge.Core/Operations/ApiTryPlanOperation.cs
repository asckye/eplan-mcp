using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Operations;
/// <summary>Independent catalog/shape validation; planning never invokes the trial member.</summary>
public sealed partial class ApiTryPlanOperation(int major, IProjectCatalog catalog, IApiTryDispatcher dispatch) : Operation<ApiTryArgs, WritePlan>
{
    public override string Name => OperationNames.ApiTryPlan;
    protected override WritePlan Execute(ApiTryArgs args) => Prepare(args);
    internal WritePlan Prepare(ApiTryArgs args)
    {
        if (major != 2026 || !ApiTryCatalog.Allowed(args.Group))
        { throw new BridgeErrorException(ErrorReason.PolicyDenied, "This catalog group is excluded, unknown or unsupported on this EPLAN version."); }
        if (string.IsNullOrWhiteSpace(args.Project) || args.Parameters is null || args.Parameters.Length > 131072)
        { throw InvalidArgument("Supply an explicit project and at most 131072 characters of parameters."); }
        JObject parameters;
        try
        { parameters = Parameters(args.Parameters); }
        catch (JsonException) { throw InvalidArgument("parameters must be a JSON object without duplicate fields."); }
        if (parameters.Properties().Any(p => Kind(p.Value) == "Object" || p.Value.Type == JTokenType.Null))
        { throw InvalidArgument("Parameters accept only simple values, PointD, discovery locators, session-scoped handles and arrays of these."); }
        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        string? method = ApiTryCatalog.MethodAccess(args.Group);
        string[]? shape = ApiTryCatalog.Shape(args.Group, parameters.Properties().Select(p => p.Name));
        if (method is not null && shape is null)
        { throw InvalidArgument("No unique generated signature matches these parameter names; inspect eplan_api_get."); }
        var plan = new WritePlan { Project = project.Name, ProjectPath = project.Path, Details = new() { ["unverified"] = "true", ["status"] = "not_dispatchable_yet" } };
        if (shape is null)
        { return plan; }
        bool setter = method is null && parameters.ContainsKey("value");
        JToken? value = setter ? parameters["value"] : null;
        bool write = setter || method == "write";
        if (!EntryParameters(args.Group, shape, parameters, setter, project.Path))
        { throw InvalidArgument("Use the exact generated parameter shape and a target locator from eplan_object_find or session-scoped handle; no coercion is allowed."); }
        if (ApiTryCatalog.Service(args.Group) && !project.Current && !shape.Skip(2).Any(n => ApiTryCatalog.ParameterShape(shape[1], n) is { } s && ApiTryCatalog.Kinds(s[s[0] == "array" ? 1 : 0]).Length > 0 && (s[0] != "array" || parameters[n]!.HasValues)))
        { throw new BridgeErrorException(ErrorReason.PolicyDenied, "A service without explicit object arguments requires the target scratch project to be current."); }
        args.Project = project.Path;
        if (!ApiTrySettingsValidation.Valid(args.Group, n => parameters[n]?.ToString()))
        { throw InvalidArgument("Use enumerated settings paths, nonnegative indexes, offset <= 1000000 and limit 1..200."); }
        dispatch.CheckTarget(args);
        Dictionary<string, string> before = ApiTryFiles.Observe(args, dispatch.Inspect(args), planning: true);
        plan.Fingerprint = Fingerprint.Of([args.Group, args.Project, args.Parameters, .. before.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => JsonConvert.SerializeObject(p))]);
        plan.Details["before"] = JsonConvert.SerializeObject(before);
        plan.Details["rawParameters"] = args.Parameters;
        plan.Details["handles"] = args.HandleSources;
        plan.Details["status"] = "dispatchable";
        plan.Changes = [new() { Action = write ? "write" : "read", Kind = "unverified API", Name = args.Group, Property = shape[1], NewValue = setter ? value!.ToString(Formatting.None) : null }];
        return plan;
    }
    internal static bool ValidValue(string[]? shape, JToken value) => shape?[0] == "array"
        ? value is JArray array && array.Count <= 200 && array.All(v => ValidValue(shape.Skip(1).ToArray(), v))
        : ApiTryCatalog.ValidValue(shape, Kind(value), value.Type == JTokenType.String ? (string)value! : value.ToString(Formatting.None));
    private static string Kind(JToken value) => value is JObject obj ? Handle(obj) ? "handle" : Locator(obj) ? "locator" : obj.Count == 2 && obj["x"] is { } x && obj["y"] is { } y && ValidValue(["System.Double"], x) && ValidValue(["System.Double"], y) ? "PointD" : "Object" : value.Type.ToString();
    private static bool Handle(JObject target) => target.Count == 2 && target["handle"]?.Type == JTokenType.String && target["type"]?.Type == JTokenType.String && ApiObjectHandle.Valid((string?)target["handle"], (string?)target["type"]);
    private static bool Locator(JObject target) => target.Count == 5 && !target.Properties().Any(p => p.Name is not ("project" or "page" or "type" or "oid" or "digest"))
        && target["project"]?.Type == JTokenType.String && target["page"]?.Type == JTokenType.String && target["type"]?.Type == JTokenType.Integer && target["oid"]?.Type == JTokenType.Integer
        && target["digest"]?.Type == JTokenType.String && System.Text.RegularExpressions.Regex.IsMatch((string)target["digest"]!, "\\A[0-9a-f]{64}\\z")
        && long.TryParse(target["type"]!.ToString(), out long type) && type is >= 1 and <= int.MaxValue && long.TryParse(target["oid"]!.ToString(), out long oid) && oid > 0;
    internal static JObject Parameters(string raw)
    {
        using var reader = new JsonTextReader(new StringReader(raw)) { DateParseHandling = DateParseHandling.None, MaxDepth = 8 };
        JObject parsed = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        return reader.Read() ? throw new JsonReaderException("Additional parameter content.") : parsed;
    }
}
