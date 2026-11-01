using System.Text.Json;
using EplanMcp.Host.Tools;
namespace EplanMcp.Host.Tests.Tools;

public sealed partial class ApiTryContractTests
{
    [Theory]
    [InlineData("h:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "Eplan.EplApi.DataModel.Pin", true)]
    [InlineData("h:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "Eplan.EplApi.DataModel.Pin", false)]
    [InlineData("h:abc", "Eplan.EplApi.DataModel.Pin", false)]
    [InlineData("h:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "Eplan.EplApi.Unknown", false)]
    public void HandleTargetUsesStrictWireShape(string handle, string type, bool valid)
    {
        JsonElement p = JsonSerializer.SerializeToElement(new { target = new { handle, type } });
        Assert.Equal(valid, ApiTryTools.MethodParameters("O:Eplan.EplApi.DataModel.ConnectionPointPosition.StoreToObject", p));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PointDArgumentsAcceptCoordinatesOrSessionScopedHandle(bool handle)
    {
        object point = handle ? new { handle = "h:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", type = "Eplan.EplApi.Base.PointD" } : new { x = 1, y = 2 };
        JsonElement p = JsonSerializer.SerializeToElement(new { target = new { project = "Scratch", page = "", type = 1, oid = 1, digest = new string('a', 64) }, pnt = point });
        Assert.True(ApiTryTools.MethodParameters("O:Eplan.EplApi.DataModel.Function.IsPointInsideLogicalArea", p));
    }

    [Fact]
    public void ReturnedEntryHandleCanReadButCannotWriteOrActAsAViaParent()
    {
        const string group = "O:Eplan.EplApi.DataModel.Project.DeviceTagSettings.EnableSyntaxCheck";
        var target = new { handle = "h:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", type = "Eplan.EplApi.DataModel.Project.DeviceTagSettings" };
        Assert.True(ApiTryTools.MethodParameters(group, JsonSerializer.SerializeToElement(new { target })));
        Assert.False(ApiTryTools.MethodParameters(group, JsonSerializer.SerializeToElement(new { target, value = true })));
        Assert.False(ApiTryTools.MethodParameters(group, JsonSerializer.SerializeToElement(new { target, via = "DeviceTagConfig" })));
    }
}
