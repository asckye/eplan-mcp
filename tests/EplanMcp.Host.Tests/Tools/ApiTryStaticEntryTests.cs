using System.Text.Json;
using EplanMcp.Host.Tools;
namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class ApiTryStaticEntryTests
{
    [Theory]
    [InlineData("DocumentTypeManager.IsLogicalPageDocType", "\"nPageType\":\"Circuit\"", false, true)]
    [InlineData("DocumentTypeManager.IsLogicalPageDocType", "\"nPageType\":\"circuit\"", false, false)]
    [InlineData("DocumentTypeManager.IsLogicalPageDocType", "\"nPageType\":\"Circuit\"", true, false)]
    [InlineData("ProjectSettings.GetBoolSetting", "\"via\":\"Settings\",\"strSettingPath\":\"PROJECT.Test\",\"nIdx\":0", true, true)]
    [InlineData("ProjectSettings.GetBoolSetting", "\"strSettingPath\":\"PROJECT.Test\",\"nIdx\":0", true, false)]
    [InlineData("ProjectSettings.GetBoolSetting", "\"via\":\"Settings\",\"strSettingPath\":\"PROJECT.Test\",\"nIdx\":\"0\"", true, false)]
    [InlineData("ProjectSettings.GetBoolSetting", "\"via\":\"Settings.Project.Settings\",\"strSettingPath\":\"PROJECT.Test\",\"nIdx\":0", true, false)]
    [InlineData("Graphics.GraphicalLayer.Name", "\"via\":\"LayerTable.Layer\",\"via_key\":\"EPLAN100\"", true, true)]
    [InlineData("Graphics.GraphicalLayer.Name", "\"via\":\"LayerTable.Layer\",\"via_key\":1", true, false)]
    [InlineData("Graphics.GraphicalLayer.Name", "\"via\":\"LayerTable.Layer\"", true, false)]
    [InlineData("Project.DeviceTagSettings.EnableSyntaxCheck", "\"via\":\"DeviceTagConfig\",\"value\":true", true, false)]
    [InlineData("Page.Name", "\"via\":\"Page\",\"value\":\"new\"", true, true)]
    [InlineData("Page.Name", "\"via\":\"Page\",\"unknown\":true", true, false)]
    public void StaticAndPropertyReceiverValidationIsClosed(string member, string fields, bool target, bool valid)
    {
        using JsonDocument p = JsonDocument.Parse("{" + (target ? "\"target\":{\"project\":\"Scratch\",\"page\":\"\",\"type\":1,\"oid\":1,\"digest\":\"" + new string('a', 64) + "\"}," : "") + fields + "}");
        Assert.Equal(valid, ApiTryTools.MethodParameters("O:Eplan.EplApi.DataModel." + member, p.RootElement));
    }
}
