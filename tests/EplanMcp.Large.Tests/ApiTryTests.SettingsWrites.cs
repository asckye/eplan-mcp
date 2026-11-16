using System.Text.Json;
namespace EplanMcp.Large.Tests;

public sealed partial class ApiTryTests
{
    /// <summary>
    /// ADR-0059 step 3b on the live EPLAN: a USER setting is written, read back and restored at once (WriteSetting);
    /// a PROJECT setting of the scratch project stays and is written back by value: EPLAN refuses to undo it (live
    /// 2026-11-16, TrObjMgmt "cannot execute undo" with a modal message), so the test never undoes a settings write.
    /// </summary>
    [Fact]
    public async Task LargeWrite_UserSettingIsRestored_ProjectSettingStays()
    {
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        JsonElement project = await Locator(selection.Project, "Project");

        object user = new { target = project, node = "USER.TRANSLATEGUI", strSettingPath = "DATABASE_NAME", nIdx = 0 };
        string userBefore = (await Call(selection.Project, "O:Eplan.EplApi.Base.Settings.GetStringSetting", user)).GetProperty("value").GetString()!;
        JsonElement written = await Call(selection.Project, "O:Eplan.EplApi.Base.Settings.SetStringSetting",
            new { target = project, node = "USER.TRANSLATEGUI", strSettingPath = "DATABASE_NAME", strValue = userBefore + "_mcp", nIdx = 0 });
        TestContext.Current.TestOutputHelper!.WriteLine("USER write: " + written.GetRawText());
        Assert.Equal("committed", written.GetProperty("execution").GetString());
        using (JsonDocument value = JsonDocument.Parse(written.GetProperty("value").GetString()!))
        {
            Assert.Equal(userBefore + "_mcp", value.RootElement.GetProperty("readBack").GetString());
            Assert.True(value.RootElement.GetProperty("restored").GetBoolean());
        }
        Assert.Equal(userBefore, (await Call(selection.Project, "O:Eplan.EplApi.Base.Settings.GetStringSetting", user)).GetProperty("value").GetString());

        object scoped = new { target = project, node = "PROJECT.TRANSLATEGUI", strSettingPath = "SOURCE_LANGUAGE", nIdx = 0 };
        string projectBefore = (await Call(selection.Project, "ProjectSettings.GetStringSetting", scoped)).GetProperty("value").GetString()!;
        string next = projectBefore == "de_DE" ? "en_US" : "de_DE";
        JsonElement set = await Call(selection.Project, "ProjectSettings.SetStringSetting",
            new { target = project, node = "PROJECT.TRANSLATEGUI", strSettingPath = "SOURCE_LANGUAGE", strValue = next, nIdx = 0 });
        TestContext.Current.TestOutputHelper.WriteLine("PROJECT write: " + set.GetRawText());
        Assert.Equal(next, (await Call(selection.Project, "ProjectSettings.GetStringSetting", scoped)).GetProperty("value").GetString());
        JsonElement back = await Call(selection.Project, "ProjectSettings.SetStringSetting",
            new { target = project, node = "PROJECT.TRANSLATEGUI", strSettingPath = "SOURCE_LANGUAGE", strValue = projectBefore, nIdx = 0 });
        using (JsonDocument value = JsonDocument.Parse(back.GetProperty("value").GetString()!))
        {
            Assert.Equal(next, value.RootElement.GetProperty("before").GetString());
            Assert.False(value.RootElement.GetProperty("restored").GetBoolean());
        }
        Assert.Equal(projectBefore, (await Call(selection.Project, "ProjectSettings.GetStringSetting", scoped)).GetProperty("value").GetString());
    }
}
