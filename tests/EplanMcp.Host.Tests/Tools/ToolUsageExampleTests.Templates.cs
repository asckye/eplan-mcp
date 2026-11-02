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
                string[]? receiver = ApiTryCatalog.Settings(entry.Row.Group) && !parameters.TryGetProperty("via", out _) && !parameters.GetProperty("target").TryGetProperty("handle", out _)
                    ? ["Eplan.EplApi.DataModel.Project"] : ApiTryCatalog.ReceiverShape(shape[1]);
                bool via = parameters.TryGetProperty("via", out _);
                if (receiver is ["via", var handleType] && !via)
                {
                    JsonElement handle = parameters.GetProperty("target");
                    Assert.True(ApiTryCatalog.HandleType(handleType));
                    Assert.Equal(handleType, handle.GetProperty("type").GetString());
                    Assert.True(ApiObjectHandle.Valid(handle.GetProperty("handle").GetString(), handleType));
                    Assert.Equal("handle", template["arguments"]!["parameters"]!["properties"]!["target"]!["kind"]!.GetValue<string>());
                }
                else
                { Assert.Equal(receiver is ["via", _], via); }
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
                    Assert.True(ApiTryTools.ValidValue(type, parameter.Value), template.ToJsonString());
                }
            }
        }
        Assert.All(ApiTryCatalog.PropertyGroups, g => Assert.Contains(g, seen));
        Assert.All(index.Data.Entries.Where(e => ApiTryCatalog.Methods(e.Row.Group).Length > 0), e =>
        {
            if (seen.Contains(e.Row.Group))
            { return; }
            var templates = ApiCallTemplates.For(e);
            Assert.NotEmpty(templates);
            Assert.All(templates, t =>
            {
                Assert.False(t["dispatchable"]!.GetValue<bool>());
                Assert.Equal("refused_unsafe", t["refusal_reason"]!.GetValue<string>());
                Assert.NotNull(ApiTryCatalog.Refused(e.Row.Group, t["access"]!.GetValue<string>()) ?? ApiTryCatalog.Refused(e.Row.Group, "call"));
            });
        });
        Assert.Contains("O:Eplan.EplApi.DataModel.DocumentTypeManager.IsLogicalPageDocType", seen);
        Assert.Contains("O:Eplan.EplApi.DataModel.ProjectSettings.GetBoolSetting", seen);
        Assert.NotEmpty(seen);
    }

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
