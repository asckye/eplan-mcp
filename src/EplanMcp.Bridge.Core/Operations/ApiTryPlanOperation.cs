using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Operations;
/// <summary>Independent catalog/shape validation; planning never invokes the trial member.</summary>
public sealed class ApiTryPlanOperation(int major, IProjectCatalog catalog, IApiTryDispatcher dispatch) : Operation<ApiTryArgs, WritePlan>
{
    public override string Name => OperationNames.ApiTryPlan;
    protected override WritePlan Execute(ApiTryArgs args) => Prepare(args);
    internal WritePlan Prepare(ApiTryArgs args)
    {
        if (major != 2026 || !ApiTryCatalog.Allowed(args.Group))
        { throw new BridgeErrorException(ErrorReason.PolicyDenied, "This catalog group is excluded, unknown or unsupported on this EPLAN version."); }
        if (string.IsNullOrWhiteSpace(args.Project) || args.Parameters is null || args.Parameters.Length > 2048)
        { throw InvalidArgument("Supply an explicit project and at most 2048 characters of parameters."); }
        JObject parameters;
        try
        { parameters = Parameters(args.Parameters); }
        catch (JsonException) { throw InvalidArgument("parameters must be a JSON object without duplicate fields."); }
        if (parameters.Properties().Any(p => p.Value.Type is not (JTokenType.String or JTokenType.Integer or JTokenType.Float or JTokenType.Boolean)
            && (p.Name != "target" || p.Value is not JObject locator || locator.Count != 5 || locator.Properties().Any(f => f.Name is not ("project" or "page" or "type" or "oid" or "digest")) || locator.Values().Any(v => v.Type is not (JTokenType.String or JTokenType.Integer)))))
        { throw InvalidArgument("Parameters accept only simple values and discovery locators."); }
        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        string[]? shape = ApiTryCatalog.Shape(args.Group);
        var plan = new WritePlan { Project = project.Name, ProjectPath = project.Path, Details = new() { ["unverified"] = "true", ["status"] = "not_dispatchable_yet" } };
        if (shape is null)
        { return plan; }
        if (parameters.Count != shape.Length - 1 || parameters.Properties().Any(p => p.Name != "target" && (!shape.Skip(2).Contains(p.Name, StringComparer.Ordinal) || p.Value.Type != JTokenType.Boolean))
            || parameters["target"] is not JObject target || target.Count != 5 || target.Properties().Any(p => p.Name is not ("project" or "page" or "type" or "oid" or "digest"))
            || target["project"]?.Type != JTokenType.String || target["page"]?.Type != JTokenType.String || target["type"]?.Type != JTokenType.Integer || target["oid"]?.Type != JTokenType.Integer
            || target["digest"]?.Type != JTokenType.String || !System.Text.RegularExpressions.Regex.IsMatch((string)target["digest"]!, "\\A[0-9a-f]{64}\\z")
            || !long.TryParse(target["type"]!.ToString(), out long type) || type is < 1 or > int.MaxValue || !long.TryParse(target["oid"]!.ToString(), out long oid) || oid < 1
            || !ProjectResolver.SamePath(project.Path, (string)target["project"]!))
        { throw InvalidArgument("Use the exact generated parameter shape and a target locator from eplan_object_find; no coercion is allowed."); }
        args.Project = project.Path;
        dispatch.CheckTarget(args);
        Dictionary<string, string> before = dispatch.Inspect(project.Path);
        plan.Fingerprint = Fingerprint.Of([args.Group, args.Project, args.Parameters, .. before.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => JsonConvert.SerializeObject(p))]);
        plan.Details["before"] = JsonConvert.SerializeObject(before);
        plan.Details["rawParameters"] = args.Parameters;
        plan.Details["status"] = "dispatchable";
        plan.Changes = [new() { Action = "try", Kind = "unverified API", Name = args.Group, Property = shape[1], NewValue = args.Parameters }];
        return plan;
    }
    internal static JObject Parameters(string raw)
    {
        using var reader = new JsonTextReader(new StringReader(raw)) { DateParseHandling = DateParseHandling.None, MaxDepth = 8 };
        JObject parsed = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        return reader.Read() ? throw new JsonReaderException("Additional parameter content.") : parsed;
    }
}
