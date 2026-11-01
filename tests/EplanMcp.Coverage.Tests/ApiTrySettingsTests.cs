using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Small")]
public sealed class ApiTrySettingsTests
{
    private static ApiEntry Entry(string name, string returns = "System.Void", ApiParameter[]? parameters = null) => new(
        new("M:Test", "O:Eplan.EplApi.Base.Settings." + name, "method", name, "registered", null, [], false),
        "Eplan.EplApi.Base", "T:Eplan.EplApi.Base.Settings", name, null, [], [new("overload", "M:Test", Parameters: parameters ?? [], ReturnType: returns)]);

    [Theory]
    [InlineData("SetStringSetting")]
    [InlineData("Set")]
    [InlineData("Save")]
    [InlineData("Load")]
    [InlineData("ImportSchemes")]
    [InlineData("ExportSchemes")]
    [InlineData("ResetNode")]
    [InlineData("ReadSettings")]
    [InlineData("WriteSetting")]
    [InlineData("CopyScheme")]
    [InlineData("AddBoolDefault")]
    [InlineData("DeleteSetting")]
    [InlineData("ForceReload")]
    public void EverySettingsMutationIsExcludedBeforeShapeSelection(string name)
    {
        ApiEntry e = Entry(name);
        Assert.Equal("settings_write", ApiTryGenerator.SettingsExclusion(e, e.Details[0]));
    }

    [Fact]
    public void ProjectSettingsGetterIsNotASetMethod()
    {
        const string id = "P:Eplan.EplApi.DataModel.Project.Settings";
        ApiDetail getter = new("overload", id, Parameters: [], ReturnType: "Eplan.EplApi.DataModel.ProjectSettings", Accessors: "get");
        ApiEntry e = new(new(id, "O:Eplan.EplApi.DataModel.Project.Settings", "property", "Project.Settings", "registered", null, [], false),
            "Eplan.EplApi.DataModel", "T:Eplan.EplApi.DataModel.Project", "Settings", null, [], [getter]);
        Assert.False(ApiTryGenerator.SettingsWrite(e));
        Assert.Null(ApiTryGenerator.SettingsExclusion(e, getter));
    }

    public static TheoryData<string, string> AdapterShapes => new()
    {
        { "Base.SchemeSetting.CheckIfSchemeExists", "node,strName" },
        { "Base.SchemeSetting.CountSetting", "node,strSettingPath" },
        { "Base.SchemeSetting.Description", "node" },
        { "Base.SchemeSetting.GetBoolSetting", "node,strSettingPath,nIdx" },
        { "Base.SchemeSetting.GetCount", "node" },
        { "Base.SchemeSetting.GetDoubleSetting", "node,strSettingPath,nIdx" },
        { "Base.SchemeSetting.GetLastUsed", "node" },
        { "Base.SchemeSetting.GetLocalizedNameSettingPath", "node" },
        { "Base.SchemeSetting.GetMultiLangStringSetting", "node,strSettingPath,nIdx" },
        { "Base.SchemeSetting.GetName", "node" },
        { "Base.SchemeSetting.GetNumericSetting", "node,strSettingPath,nIdx" },
        { "Base.SchemeSetting.GetStringSetting", "node,strSettingPath,nIdx" },
        { "Base.SchemeSetting.MLangName", "node" },
        { "Base.SchemeSetting.ReadOnly", "node" },
        { "Base.SettingNode.ExistSetting", "node,strSettingPath" },
        { "Base.SettingNode.GetBoolSetting", "node,strSettingPath,idx" },
        { "Base.SettingNode.GetCountOfNodes", "node" },
        { "Base.SettingNode.GetCountOfSettings", "node" },
        { "Base.SettingNode.GetDoubleSetting", "node,strSettingPath,idx" },
        { "Base.SettingNode.GetListOfAllSettings", "node,offset,limit" },
        { "Base.SettingNode.GetListOfNodes", "node,offset,limit" },
        { "Base.SettingNode.GetListOfSettings", "node,offset,limit" },
        { "Base.SettingNode.GetMultiLangStringSetting", "node,strSettingPath,idx" },
        { "Base.SettingNode.GetNodePath", "node" },
        { "Base.SettingNode.GetNodePathDot", "node" },
        { "Base.SettingNode.GetNumericSetting", "node,strSettingPath,nIdx" },
        { "Base.SettingNode.GetStringSetting", "node,strSettingPath,idx" },
        { "Base.Settings.ExistSetting", "node,strSettingPath" },
        { "Base.Settings.GetBoolDefault", "node,strSettingPath,nIdx" },
        { "Base.Settings.GetBoolSetting", "node,strSettingPath,nIdx" },
        { "Base.Settings.GetCountOfDefaultValues", "node,strSettingPath" },
        { "Base.Settings.GetCountOfValues", "node,strSettingPath" },
        { "Base.Settings.GetDescription", "node,strSettingPath" },
        { "Base.Settings.GetDoubleDefault", "node,strSettingPath,nIdx" },
        { "Base.Settings.GetDoubleSetting", "node,strSettingPath,nIdx" },
        { "Base.Settings.GetExpandedStringSetting", "node,strSettingPath,nIdx" },
        { "Base.Settings.GetMultiLangStringDefault", "node,strSettingPath,nIdx" },
        { "Base.Settings.GetMultiLangStringSetting", "node,strSettingPath,nIdx" },
        { "Base.Settings.GetNumericDefault", "node,strSettingPath,nIdx" },
        { "Base.Settings.GetNumericSetting", "node,strSettingPath,nIdx" },
        { "Base.Settings.GetStringDefault", "node,strSettingPath,nIdx" },
        { "Base.Settings.GetStringSetting", "node,strSettingPath,nIdx" },
        { "Base.Settings.GetTypeOfSetting", "node,strSettingPath" },
        { "Base.Settings.IsMainNodeReadOnly", "node,strSettingPath" },
        { "DataModel.Project.DeviceTagSettings.AllowSpecialCharacters", "" },
        { "DataModel.Project.DeviceTagSettings.AllowUserCharacters", "" },
        { "DataModel.Project.DeviceTagSettings.AllowedLetters", "" },
        { "DataModel.Project.DeviceTagSettings.EnableSyntaxCheck", "" },
        { "DataModel.Project.DeviceTagSettings.PlantDesignationNumbersOnly", "" },
        { "DataModel.Project.DeviceTagSettings.UserCharacters", "" },
        { "DataModel.Project.Settings", "" },
        { "DataModel.ProjectSettingNode.GetListOfAllSettings", "node,offset,limit" },
        { "DataModel.ProjectSettingNode.GetListOfNodes", "node,offset,limit" },
        { "DataModel.ProjectSettingNode.GetListOfSettings", "node,offset,limit" },
        { "DataModel.ProjectSettingNode.GetNodePath", "node" },
        { "DataModel.ProjectSettingNode.GetNodePathDot", "node" },
        { "DataModel.ProjectSettings.ExistSetting", "node,strSettingPath" },
        { "DataModel.ProjectSettings.GetBoolSetting", "node,strSettingPath,nIdx" },
        { "DataModel.ProjectSettings.GetCountOfValues", "node,strSettingPath" },
        { "DataModel.ProjectSettings.GetDoubleSetting", "node,strSettingPath,nIdx" },
        { "DataModel.ProjectSettings.GetExpandedStringSetting", "node,strSettingPath,nIdx" },
        { "DataModel.ProjectSettings.GetMultiLangStringSetting", "node,strSettingPath,nIdx" },
        { "DataModel.ProjectSettings.GetNumericSetting", "node,strSettingPath,nIdx" },
        { "DataModel.ProjectSettings.GetStringSetting", "node,strSettingPath,nIdx" },
        { "DataModel.ProjectSettings.GetTypeOfSetting", "node,strSettingPath" },
    };

    [Theory]
    [MemberData(nameof(AdapterShapes))]
    public void EveryAdapterShapeIsReadOnlyAndUsesProjectAnchor(string name, string fields)
    {
        string group = "O:Eplan.EplApi." + name;
        string[] names = fields.Length == 0 ? ["target"] : ["target", .. fields.Split(',')];
        string[] shape = ApiTryCatalog.Shape(group, names)!;
        Assert.NotNull(shape);
        Assert.Equal(names.Skip(1), shape.Skip(2));
        Assert.True(ApiTryCatalog.Allowed(group));
        Assert.Equal("read", ApiTryCatalog.MethodAccess(group));
        Assert.Equal(["project"], ApiTryCatalog.TargetKinds(group));
        Assert.Null(ApiTryCatalog.ValueShape(group));
        Assert.Null(ApiTryCatalog.Shape(group, [.. names, "value"]));
        Assert.All(shape.Skip(2), n => Assert.NotNull(ApiTryCatalog.ParameterShape(shape[1], n)));
    }

    [Fact]
    public void CatalogExclusionsAndUnknownReadShapesNeverBecomeDispatchable()
    {
        ApiEntry e = Entry("GetStringSetting", "System.String", [new("path", "System.String")]);
        Assert.Null(ApiTryGenerator.SettingsExclusion(e, e.Details[0]));
        Assert.Equal("catalog:unsafe", ApiTryGenerator.SettingsExclusion(e, e.Details[0] with { ExcludedReason = "unsafe" }));
        Assert.Equal("static_or_nonpublic", ApiTryGenerator.SettingsExclusion(e, e.Details[0] with { Static = true }));
        Assert.Equal("unsupported_settings_read", ApiTryGenerator.SettingsExclusion(Entry("GetUnreviewed", "System.String"), e.Details[0]));
        foreach (string method in new[] { "SetStringSetting", "ReadSettings", "WriteSetting", "RemoveAllIndexedSetting" })
        { Assert.False(ApiTryCatalog.Allowed("O:Eplan.EplApi.Base.Settings." + method)); }
    }
}

[Trait("Size", "Medium")]
public sealed class ApiTrySettingsGenerationTests
{
    [Fact]
    public void GeneratedDispatchContainsOnlyClosedSettingsReadsAndReportsAllWrites()
    {
        string root = Program.RepositoryRoot(AppContext.BaseDirectory)!;
        string dispatch = ApiTryGenerator.Dispatch(root);
        Assert.Contains("project.Settings", dispatch, StringComparison.Ordinal);
        Assert.Contains("GetListOfNodes(ref list, false)", dispatch, StringComparison.Ordinal);
        Assert.Contains("using var text = receiver.GetMultiLangStringSetting", dispatch, StringComparison.Ordinal);
        string[] rows = ApiTryGenerator.Report(root).Split('\n');
        Assert.All(rows.Where(r => r.Length > 0).GroupBy(r => r.Split('\t')[1]), g => Assert.Single(g));
        Assert.All(rows.Where(r => r.Contains("\tsettings_write", StringComparison.Ordinal)), row =>
        {
            string signature = row.Split('\t')[1];
            Assert.DoesNotContain(JsonSerializer.Serialize(signature), dispatch, StringComparison.Ordinal);
            if (!signature.EndsWith(":set", StringComparison.Ordinal))
            {
                string name = signature.Split('(')[0].Split('.')[^1];
                Assert.DoesNotContain("receiver." + name + "(", dispatch, StringComparison.Ordinal);
                Assert.False(ApiTryCatalog.Allowed(row.Split('\t')[0]));
            }
        });
        Assert.DoesNotContain("System.Reflection", dispatch, StringComparison.Ordinal);
        Assert.Contains(rows, r => r.StartsWith("O:Eplan.EplApi.Base.SettingNode.GetListOfNodes\t", StringComparison.Ordinal) && r.EndsWith("\tread\tsettings_read_only", StringComparison.Ordinal));
    }
}
