using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Eplan.Adapters;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

[Trait("Size", "Small")]
public sealed class DeviceNamesCategoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadDevice_ReturnsPointAndNullableCategoryWhenNativeMetadataIsUnreadable(bool unreadable)
    {
#pragma warning disable CA2000 // Fake persistent objects are borrowed from EPLAN.
        var page = new Page { Name = "=K1+A2&EFA1/4" };
        var card = new Function { Name = "=K1+A2-KE1", IsMainFunction = true, Page = page };
        var point = new Function(2) { Name = card.Name + ":1.1", FunctionCategory = FunctionCategory.PLCTerminal, Page = page };
        point.CategoryReadError = unreadable ? new BaseException("Unreadable category") : null;
        page.AllPlacements = [card, point];
        EplanProjects.Project = new Project(page);
#pragma warning restore CA2000
        var read = new EplanDeviceRenamer().ReadDevice("project", card.Name)!;
        var channel = Assert.Single(read.Functions, f => f.Name == point.Name);
        Assert.Equal(unreadable ? null : "PLCTerminal", channel.Category);
        Assert.Equal(page.Name, channel.Page);
    }
}
