namespace EplanMcp.Contracts.Tests;

[Trait("Size", "Small")]
public sealed class ApiTrySettingsValidationTests
{
    [Theory]
    [InlineData("Base.SettingNode.GetStringSetting", "USER", "0", true)]
    [InlineData("Base.SettingNode.GetStringSetting", "STATION.A", "1000000", true)]
    [InlineData("Base.SettingNode.GetStringSetting", "PROJECT.A", "0", true)]
    [InlineData("DataModel.ProjectSettings.GetStringSetting", "PROJECT", "0", true)]
    [InlineData("DataModel.ProjectSettings.GetStringSetting", "USER.A", "0", false)]
    [InlineData("Base.Settings.GetStringSetting", "PROJECT.A", "0", false)]
    [InlineData("Base.Settings.GetStringSetting", "COMPANY.A", "0", false)]
    [InlineData("Base.Settings.GetStringSetting", "INSTALL.A", "0", false)]
    [InlineData("Base.Settings.GetStringSetting", "user.A", "0", false)]
    [InlineData("Base.Settings.GetStringSetting", "USER..A", "0", false)]
    [InlineData("Base.Settings.GetStringSetting", "USER.A\n", "0", false)]
    [InlineData("Base.Settings.GetStringSetting", "USER.A", "-1", false)]
    [InlineData("Base.Settings.GetStringSetting", "USER.A", "1000001", false)]
    [InlineData("Base.Settings.GetStringSetting", "USER.A", "0.5", false)]
    public void ScopeAndIndexValidationRunsWithoutNativeSettings(string member, string node, string index, bool valid)
    {
        var parameters = new Dictionary<string, string> { ["node"] = node, ["strSettingPath"] = "Leaf", ["nIdx"] = index };
        Assert.Equal(valid, ApiTrySettingsValidation.Valid("O:Eplan.EplApi." + member, n => parameters.GetValueOrDefault(n)));
    }

    [Theory]
    [InlineData("0", "1", true)]
    [InlineData("1000000", "200", true)]
    [InlineData("-1", "10", false)]
    [InlineData("1000001", "10", false)]
    [InlineData("0", "0", false)]
    [InlineData("0", "201", false)]
    public void EnumerationPagingIsBounded(string offset, string limit, bool valid)
    {
        var p = new Dictionary<string, string> { ["node"] = "PROJECT", ["offset"] = offset, ["limit"] = limit };
        Assert.Equal(valid, ApiTrySettingsValidation.Valid("O:Eplan.EplApi.DataModel.ProjectSettingNode.GetListOfNodes", n => p.GetValueOrDefault(n)));
    }

    [Fact]
    public void EveryPathSegmentRequiresExactFreshEnumerationAndDoesNotFallBack()
    {
        var queried = new List<string>();
        IEnumerable<string> Children(string parent)
        {
            queried.Add(parent);
            return parent == "USER" ? ["A"] : parent == "USER.A" ? ["B"] : [];
        }
        Assert.True(ApiTrySettingsValidation.Enumerated("USER.A.B", Children));
        Assert.Equal(["USER", "USER.A"], queried);
        queried.Clear();
        Assert.False(ApiTrySettingsValidation.Enumerated("USER.a.B", Children));
        Assert.Equal(["USER"], queried);
        Assert.False(ApiTrySettingsValidation.Enumerated("COMPANY.A", Children));
        Assert.False(ApiTrySettingsValidation.Enumerated("USER..B", Children));
        Assert.True(ApiTrySettingsValidation.Enumerated("PROJECT", _ => throw new InvalidOperationException()));
    }
}
