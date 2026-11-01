using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class ToolUsageExampleTests
{
    [Fact]
    public void EveryGeneratedTemplateExampleMatchesToolSchemaAndDispatchShape()
    {
        JsonElement schema = Tools.Single(t => t.Name == ApiTryTools.Tool).InputSchema;
        ApiIndex index = ApiIndex.Load();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ApiEntry entry in index.Data.Entries)
        {
            foreach (JsonObject template in ApiCallTemplates.For(entry).Where(t => t["dispatchable"]!.GetValue<bool>()))
            {
                seen.Add(entry.Row.Group);
                JsonElement example = JsonSerializer.SerializeToElement(template["example"]);
                Assert.Empty(Violations(schema, example, null));
                JsonElement parameters = example.GetProperty("parameters");
                string[] shape = ApiTryCatalog.Shape(entry.Row.Group, parameters.EnumerateObject().Select(p => p.Name))!;
                Assert.NotNull(shape);
                Assert.Equal(shape[1], template["signature"]!.GetValue<string>());
                Assert.Null(ApiTryCatalog.ExclusionReason(entry.Row.Group));
                bool setter = template["access"]!.GetValue<string>() == "write" && ApiTryCatalog.MethodAccess(entry.Row.Group) is null;
                string[]? receiver = ApiTryCatalog.ReceiverShape(shape[1]);
                bool via = receiver is ["via", _];
                bool key = via && ApiTryCatalog.ViaKey(shape[0], parameters.GetProperty("via").GetString()!);
                Assert.Equal(shape.Skip(2).Concat(receiver is null ? Array.Empty<string>() : ["target"]).Concat(via ? ["via"] : Array.Empty<string>()).Concat(key ? ["via_key"] : Array.Empty<string>()).Concat(setter ? ["value"] : Array.Empty<string>()).Order(StringComparer.Ordinal), parameters.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
                if (via)
                {
                    string path = parameters.GetProperty("via").GetString()!;
                    Assert.Contains(template["arguments"]!["parameters"]!["properties"]!["target"]!["object_type"]!.GetValue<string>(), ApiTryCatalog.ViaKinds(shape[0], path));
                    Assert.Equal(path, template["arguments"]!["parameters"]!["properties"]!["via"]!["const"]!.GetValue<string>());
                }
                Assert.True(ApiTryTools.MethodParameters(entry.Row.Group, parameters), template.ToJsonString());
                foreach (JsonProperty parameter in parameters.EnumerateObject().Where(p => p.Name is not ("target" or "via" or "via_key")))
                {
                    string[] type = (setter && parameter.Name == "value" ? ApiTryCatalog.ValueShape(entry.Row.Group) : ApiTryCatalog.ParameterShape(shape[1], parameter.Name))!;
                    Assert.True(ApiTryCatalog.ValidValue(type, ValueKind(parameter.Value), parameter.Value.ValueKind == JsonValueKind.String ? parameter.Value.GetString()! : parameter.Value.GetRawText()), template.ToJsonString());
                }
            }
        }
        Assert.All(ApiTryCatalog.PropertyGroups, g => Assert.Contains(g, seen));
        Assert.All(index.Data.Entries.Where(e => ApiTryCatalog.Methods(e.Row.Group).Length > 0), e => Assert.Contains(e.Row.Group, seen));
        Assert.Contains("O:Eplan.EplApi.DataModel.DocumentTypeManager.IsLogicalPageDocType", seen);
        Assert.Contains("O:Eplan.EplApi.DataModel.ProjectSettings.GetBoolSetting", seen);
        Assert.NotEmpty(seen);
    }

    private static string ValueKind(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.TryGetProperty("x", out _) ? "PointD" : "locator",
        JsonValueKind.Number => value.TryGetInt64(out _) ? "Integer" : "Float",
        JsonValueKind.False or JsonValueKind.True => "Boolean",
        _ => value.ValueKind.ToString(),
    };

    [Fact]
    public void EveryEligibleActionExampleMatchesItsSchemaAndPolicy()
    {
        JsonElement schema = Tools.Single(t => t.Name == ActionTryTools.Tool).InputSchema;
        foreach (ActionCatalogItem row in ActionCatalogReader.Official())
        {
            JsonObject template = ActionCallTemplates.For(row);
            if (template["denied"]!.GetValue<bool>())
            { continue; }
            JsonElement example = JsonSerializer.SerializeToElement(template["example"]);
            Assert.Empty(Violations(schema, example, null));
            ActionTryTools.Check(new([row], null, []), "Scratch", row.Name, row.Type, example.GetProperty("parameters"));
        }
    }
}
