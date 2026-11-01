using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class CallTemplateTests
{
    [Theory]
    [InlineData("System.String", "string")]
    [InlineData("System.Int32", "int")]
    [InlineData("System.Int64", "int")]
    [InlineData("System.Double", "double")]
    [InlineData("System.Boolean", "bool")]
    [InlineData("Eplan.EplApi.Base.PointD", "PointD")]
    [InlineData("Eplan.EplApi.DataModel.Page", "locator")]
    [InlineData("System.String[]", "array")]
    [InlineData("input_path", "input_path")]
    [InlineData("output_path", "output_path")]
    public void KindsHaveRequiredMetadataAndTypedExamples(string type, string kind)
    {
        var (description, example) = ApiCallTemplates.Parameter([type]);
        Assert.Equal(kind, description["kind"]!.GetValue<string>());
        Assert.True(description["required"]!.GetValue<bool>());
        Assert.NotNull(example);
        if (kind == "locator")
        {
            Assert.Equal("page", description["object_type"]!.GetValue<string>());
            Assert.Equal("eplan_object_find", description["source"]!["tool"]!.GetValue<string>());
            Assert.Equal("page", description["source"]!["arguments"]!["object_type"]!.GetValue<string>());
        }
    }

    [Fact]
    public void EnumArrayAndHandleRetainAllNamesAndProducers()
    {
        var (description, example) = ApiCallTemplates.Parameter(["enum", "Manual", "Automatic"]);
        Assert.Equal("Manual", example.GetValue<string>());
        Assert.Equal("[\"Manual\",\"Automatic\"]", description["allowed_values"]!.ToJsonString());
        var array = ApiCallTemplates.Parameter(["array", "enum", "A", "B"]);
        Assert.Equal("enum", array.Description["items"]!["kind"]!.GetValue<string>());
        Assert.Equal("[\"A\"]", array.Example.ToJsonString());
        var handle = ApiCallTemplates.Parameter(["handle", "Session", "O:Session.Create"]);
        Assert.Equal("Session", handle.Description["type"]!.GetValue<string>());
        Assert.Contains("handle returned by O:Session.Create", handle.Description["source"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Eplan.EplApi.DataModel.Pin.ConsumerSources", "target", "Eplan.EplApi.DataModel.Pin")]
    [InlineData("Eplan.EplApi.Base.MultiLangString.IsEqual", "otherVal", "Eplan.EplApi.Base.MultiLangString")]
    public void HandleGroupsHaveDispatchableTemplatesWithRealProducers(string name, string parameter, string type)
    {
        ApiEntry entry = ApiIndex.Load().Data.Entries.Single(e => e.Row.Group == "O:" + name);
        Assert.Null(ApiTryCatalog.ExclusionReason(entry.Row.Group));
        JsonObject[] templates = [.. ApiCallTemplates.For(entry).Where(t => t["arguments"]!["parameters"]!["properties"]![parameter]?["kind"]?.GetValue<string>() == "handle")];
        Assert.NotEmpty(templates);
        Assert.All(templates, template =>
        {
            Assert.True(template["dispatchable"]!.GetValue<bool>());
            JsonNode description = template["arguments"]!["parameters"]!["properties"]![parameter]!;
            Assert.Equal("handle", description["kind"]!.GetValue<string>());
            Assert.Equal(type, description["type"]!.GetValue<string>());
            string[] producers = ApiTryCatalog.Producers(type);
            Assert.NotEmpty(producers);
            Assert.Equal("handle returned by " + string.Join(", ", producers), description["source"]!.GetValue<string>());
            JsonElement example = JsonSerializer.SerializeToElement(template["example"]!["parameters"]);
            Assert.True(ApiTryTools.MethodParameters(entry.Row.Group, example));
        });
        if (parameter == "target")
        {
            Assert.Contains(entry.Row.Group, ApiTryCatalog.PropertyGroups);
            Assert.Contains(templates, t => t["access"]!.GetValue<string>() == "read");
            Assert.Contains(templates, t => t["access"]!.GetValue<string>() == "write");
        }
    }

    [Theory]
    [InlineData("Page.GetWriteProtectionFlagSet", "read", "nKind")]
    [InlineData("Function.IsPointInsideLogicalArea", "read", "pnt")]
    [InlineData("Graphics.PlaceHolder.ApplyRecord", "write", "strRecordName")]
    [InlineData("DocumentTypeManager.IsLogicalPageDocType", "read", "nPageType")]
    [InlineData("ProjectSettings.GetBoolSetting", "read", "strSettingPath")]
    public void RepresentativeGroupsComeFromTheGeneratedCatalog(string name, string access, string parameter)
    {
        ApiEntry entry = ApiIndex.Load().Data.Entries.Single(e => e.Row.Group == "O:Eplan.EplApi.DataModel." + name);
        JsonObject[] templates = ApiCallTemplates.For(entry);
        Assert.All(templates, t => Assert.Equal(access, t["access"]!.GetValue<string>()));
        Assert.All(templates, t => Assert.NotNull(t["arguments"]!["parameters"]!["properties"]![parameter]));
        if (parameter == "nKind")
        { Assert.Equal(ApiTryCatalog.ParameterShape(ApiTryCatalog.Methods(entry.Row.Group)[0][1], parameter)![1..], templates[0]["arguments"]!["parameters"]!["properties"]![parameter]!["allowed_values"]!.AsArray().Select(v => v!.GetValue<string>())); }
    }

    [Fact]
    public void ArrayAndOutputParameterGroupsUseGeneratedInputShapes()
    {
        ApiIndex index = ApiIndex.Load();
        const string Rotate = "O:Eplan.EplApi.HEServices.Edit.Rotate";
        JsonObject array = Assert.Single(ApiCallTemplates.For(index.Data.Entries.Single(e => e.Row.Group == Rotate)));
        JsonNode placements = array["arguments"]!["parameters"]!["properties"]!["plcmnts"]!;
        Assert.Equal("array", placements["kind"]!.GetValue<string>());
        Assert.Equal("locator", placements["items"]!["kind"]!.GetValue<string>());
        Assert.True(ApiTryTools.MethodParameters(Rotate, JsonSerializer.SerializeToElement(array["example"]!["parameters"])));

        // The installed catalog marks PointD@ as ref, not out: it remains an input and produces a named output.
        const string Outputs = "O:Eplan.EplApi.DataModel.Graphics.PolyLine.SetPointAt";
        JsonObject outputs = Assert.Single(ApiCallTemplates.For(index.Data.Entries.Single(e => e.Row.Group == Outputs)));
        Assert.Equal("PointD", outputs["arguments"]!["parameters"]!["properties"]!["point"]!["kind"]!.GetValue<string>());
        Assert.Equal(["target", "index", "point"], outputs["example"]!["parameters"]!.AsObject().Select(p => p.Key));
        Assert.True(ApiTryTools.MethodParameters(Outputs, JsonSerializer.SerializeToElement(outputs["example"]!["parameters"])));
    }

    [Theory]
    [InlineData("DocumentTypeManager.IsLogicalPageDocType", null, null)]
    [InlineData("ProjectSettings.GetBoolSetting", "Settings", "project")]
    [InlineData("Project.DeviceTagSettings.EnableSyntaxCheck", "DeviceTagConfig", "project")]
    [InlineData("Graphics.GraphicalLayer.Name", "LayerTable.Layer", "project")]
    public void StaticAndEntryTemplatesDescribeTheGeneratedReceiver(string name, string? via, string? parentKind)
    {
        ApiEntry entry = ApiIndex.Load().Data.Entries.Single(e => e.Row.Group == "O:Eplan.EplApi.DataModel." + name);
        Assert.Null(ApiTryCatalog.ExclusionReason(entry.Row.Group));
        JsonObject[] templates = ApiCallTemplates.For(entry);
        Assert.NotEmpty(templates);
        Assert.All(templates, template => Assert.True(ApiTryTools.MethodParameters(entry.Row.Group, JsonSerializer.SerializeToElement(template["example"]!["parameters"])), template.ToJsonString()));
        if (via is not null)
        {
            templates = [.. templates.Where(t => t["example"]!["parameters"]!["via"]?.GetValue<string>() == via
                && t["arguments"]!["parameters"]!["properties"]!["target"]!["object_type"]!.GetValue<string>() == parentKind)];
            Assert.NotEmpty(templates);
        }
        Assert.All(templates, template =>
        {
            JsonObject parameters = template["example"]!["parameters"]!.AsObject();
            JsonObject properties = template["arguments"]!["parameters"]!["properties"]!.AsObject();
            if (via is null)
            {
                Assert.False(parameters.ContainsKey("target"));
                Assert.False(parameters.ContainsKey("via"));
                Assert.False(parameters.ContainsKey("via_key"));
                Assert.False(properties.ContainsKey("target"));
            }
            else
            {
                Assert.Equal(via, parameters["via"]!.GetValue<string>());
                Assert.Equal(via, properties["via"]!["const"]!.GetValue<string>());
                Assert.True(properties["via"]!["required"]!.GetValue<bool>());
                Assert.Equal(parentKind, properties["target"]!["object_type"]!.GetValue<string>());
                Assert.Equal(parentKind, properties["target"]!["source"]!["arguments"]!["object_type"]!.GetValue<string>());
                bool key = ApiTryCatalog.ViaKey(ApiTryCatalog.Shape(entry.Row.Group, parameters.Select(p => p.Key))![0], via);
                Assert.Equal(key, parameters.ContainsKey("via_key"));
                Assert.Equal(key, properties.ContainsKey("via_key"));
            }
        });
    }

    [Fact]
    public void SettingsEnumerationTemplateIncludesProjectAnchorAndBoundedPagingShape()
    {
        const string group = "O:Eplan.EplApi.Base.SettingNode.GetListOfNodes";
        ApiEntry entry = ApiIndex.Load().Data.Entries.Single(e => e.Row.Group == group);
        Assert.Null(ApiTryCatalog.ExclusionReason(group));
        JsonObject template = Assert.Single(ApiCallTemplates.For(entry));
        Assert.True(template["dispatchable"]!.GetValue<bool>());
        Assert.Equal("read", template["access"]!.GetValue<string>());
        JsonObject properties = template["arguments"]!["parameters"]!["properties"]!.AsObject();
        Assert.Equal("project", properties["target"]!["object_type"]!.GetValue<string>());
        Assert.Equal("string", properties["node"]!["kind"]!.GetValue<string>());
        Assert.Equal("int", properties["offset"]!["kind"]!.GetValue<string>());
        Assert.Equal("int", properties["limit"]!["kind"]!.GetValue<string>());
        Assert.False(properties.ContainsKey("bAbsolutPath"));
        Assert.False(properties.ContainsKey("via"));
        Assert.True(ApiTryTools.MethodParameters(group, JsonSerializer.SerializeToElement(template["example"]!["parameters"])));
    }

    [Fact]
    public void SettingsTemplatesKeepBothNodeAndViaEntryShapes()
    {
        const string group = "O:Eplan.EplApi.DataModel.ProjectSettings.GetBoolSetting";
        ApiEntry entry = ApiIndex.Load().Data.Entries.Single(e => e.Row.Group == group);
        JsonObject[] templates = ApiCallTemplates.For(entry);
        Assert.Contains(templates, t => t["example"]!["parameters"]!["node"]?.GetValue<string>() == "PROJECT");
        Assert.Contains(templates, t => t["example"]!["parameters"]!["via"]?.GetValue<string>() == "Settings");
        Assert.All(templates, t => Assert.True(ApiTryTools.MethodParameters(group, JsonSerializer.SerializeToElement(t["example"]!["parameters"])), t.ToJsonString()));
    }

    [Fact]
    public void ClosestShapeDistinguishesOverloadsAndPropertySetter()
    {
        ApiIndex index = ApiIndex.Load();
        JsonObject template = ApiCallTemplates.Closest(index, "O:Eplan.EplApi.DataModel.Graphics.PlaceHolder.ApplyRecord", JsonSerializer.SerializeToElement(new { target = "bad", strRecordName = "r", bApplyPageAndProjectData = false, extra = 1 }))!;
        Assert.NotNull(template["example"]!["parameters"]!["bApplyPageAndProjectData"]);
        Assert.Equal("write", ApiCallTemplates.Closest(index, "O:Eplan.EplApi.DataModel.Page.GridSize", JsonSerializer.SerializeToElement(new { target = "bad", value = 1 }))!["access"]!.GetValue<string>());
    }

    [Fact]
    public void UndispatchableGroupExplainsItsAlternativeAndGetPagesTemplates()
    {
        ApiIndex index = ApiIndex.Load();
        ApiEntry entry = index.Data.Entries.First(e => ApiTryCatalog.Shape(e.Row.Group) is null && ApiTryCatalog.Methods(e.Row.Group).Length == 0 && e.Row.Tools.Length > 0);
        JsonObject excluded = Assert.Single(ApiCallTemplates.For(entry));
        Assert.False(excluded["dispatchable"]!.GetValue<bool>());
        Assert.NotNull(excluded["exclusion_reason"]);
        Assert.Equal(entry.Row.Tools, excluded["alternatives"]!.AsArray().Select(v => v!.GetValue<string>()));
        JsonElement result = ApiTools.Get(index, "O:Eplan.EplApi.DataModel.Page.Name", 1, cancellationToken: TestContext.Current.CancellationToken).StructuredContent!.Value;
        Assert.Equal("try_template", result.GetProperty("items")[0].GetProperty("kind").GetString());
        Assert.True(result.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public void ActionTemplatePreservesUnknownsAndDeniesUnsafeParameters()
    {
        string[] modes = ["A", "B"];
        ActionCatalogItem row = ActionCatalogReader.Create("XTest", "ui", null, null, null, [JsonSerializer.SerializeToElement(new { name = "Mode", values = modes, required = true }), JsonSerializer.SerializeToElement(new { name = "FILE" }), JsonSerializer.SerializeToElement(new { name = "Unknown" })], null, "test");
        JsonObject template = ActionCallTemplates.For(row);
        Assert.False(template["denied"]!.GetValue<bool>());
        Assert.True(template["arguments"]!["parameters"]!["FILE"]!["denied"]!.GetValue<bool>());
        Assert.Null(template["arguments"]!["parameters"]!["Unknown"]!["required"]);
        Assert.Equal("A", template["example"]!["parameters"]!["Mode"]!.GetValue<string>());
        Assert.Contains("eplan_selection_set", template["selection"]!["how"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.True(ActionCallTemplates.For(row with { Name = "ProjectOpen" })["denied"]!.GetValue<bool>());
    }

    [Fact]
    public void ValidationErrorCarriesARealTemplate()
    {
        JsonObject template = ApiCallTemplates.Closest(ApiIndex.Load(), "O:Eplan.EplApi.DataModel.Page.Name", default)!;
        var result = ToolResults.Error(ToolResults.InvalidArgumentError(["bad target"]), template);
        using JsonDocument document = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.Equal("eplan_api_try", document.RootElement.GetProperty("try_template").GetProperty("tool").GetString());
        Assert.Equal(ErrorReason.InvalidArgument, document.RootElement.GetProperty("error").GetProperty("reason").GetString());
    }
}
