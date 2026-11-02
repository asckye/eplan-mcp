using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Tests.Adapters;

[Trait("Size", "Small")]
public sealed class ApiTryResultAdapterTests
{
    private static ApiTryArgs Args(string group, string fields = "") => new()
    {
        Group = group,
        Project = @"D:\scratch\Scratch",
        Parameters = "{\"target\":{\"project\":\"D:\\\\scratch\\\\Scratch\",\"page\":\"\",\"type\":1,\"oid\":1,\"digest\":\"" + new string('a', 64) + "\"}" + fields + "}",
    };

    [Fact]
    public void ProjectSettingsWithOnlyProjectLocatorReturnsTextInsteadOfHandle()
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.Project.Settings");
        Assert.True(ApiTryCatalog.Settings(args.Group));
        Assert.NotNull(ApiTryCatalog.HandleShape(args.Group));
        Assert.Single(JObject.Parse(args.Parameters).Properties());
        Assert.Equal(args.Project, JObject.Parse(args.Parameters)["target"]!["project"]!.Value<string>());
        var dispatcher = new EplanApiTryDispatcher();
        object? value = dispatcher.InvokeResult(args);
        Assert.Equal("PROJECT", Assert.IsType<string>(value));
        Assert.Equal(JTokenType.String, JToken.FromObject(value).Type);
        Assert.Equal(0, dispatcher.ReturnedHandles);
    }

    [Theory]
    [InlineData("O:Eplan.EplApi.DataModel.Project.DeviceTagSettings.AllowedLetters", "", "True")]
    [InlineData("O:Eplan.EplApi.Base.SettingNode.GetListOfNodes", ",\"node\":\"USER\",\"offset\":0,\"limit\":10", "{\"items\":[\"A\"],\"total\":1,\"nextOffset\":null}")]
    [InlineData("O:Eplan.EplApi.DataModel.ProjectSettings.GetStringSetting", ",\"node\":\"PROJECT\",\"strSettingPath\":\"Leaf\",\"nIdx\":0", "setting text")]
    [InlineData("O:Eplan.EplApi.DataModel.ProjectSettings.GetStringSetting", ",\"via\":\"Settings\",\"strSettingPath\":\"Leaf\",\"nIdx\":0", "setting text")]
    public void SettingsScalarsListsAndPropertyEntriesKeepTheirText(string group, string fields, string expected)
        => Assert.Equal(expected, Assert.IsType<string>(new EplanApiTryDispatcher().InvokeResult(Args(group, fields))));

    [Fact]
    public void NonSettingsNativeResultStillReturnsHandle()
    {
        ApiObjectHandle value = Assert.IsType<ApiObjectHandle>(new EplanApiTryDispatcher().InvokeResult(Args("O:Eplan.EplApi.DataModel.Connection.StartPin")));
        Assert.Equal("Eplan.EplApi.DataModel.Pin", value.Type);
        Assert.True(ApiObjectHandle.Valid(value.Handle, value.Type));
    }

    [Fact]
    public void NonSettingsCollectionStillReturnsIndependentElementHandles()
    {
        JArray value = Assert.IsType<JArray>(new EplanApiTryDispatcher().InvokeResult(Args("O:Eplan.EplApi.DataModel.Pin.Connections")));
        JToken element = Assert.Single(value);
        Assert.Equal("Eplan.EplApi.DataModel.Connection", element["type"]!.Value<string>());
        Assert.Equal("h:" + new string('b', 32), element["handle"]!.Value<string>());
    }
}
