using EplanMcp.Workbench.Settings;
using TiaOpenness.Gui.Localization;
using TiaOpenness.Gui.Themes;

namespace EplanMcp.Workbench.Tests.Settings;

/// <summary>The settings file in the data root: kept whole, and never a reason not to start.</summary>
[Trait("Size", "Medium")]
public sealed class WorkbenchSettingsTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("eplanmcp-workbench-settings-");

    private string File => new WorkbenchDataLocations(_root.FullName).Settings;

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public void TheSettings_RoundTripThroughTheWorkbenchFolderOfTheDataRoot()
    {
        var chosen = new WorkbenchSettings(AppLanguage.English, AppTheme.Dark, StartAtLogin: true);

        Assert.True(chosen.Save(File));

        Assert.Equal(Path.Combine(_root.FullName, "workbench", "settings.json"), File);
        Assert.Equal(chosen, WorkbenchSettings.Load(File));
        Assert.Contains("\"theme\": \"dark\"", System.IO.File.ReadAllText(File), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("""{"language":"klingon"}""")]
    [InlineData("""{"theme":2}""")]
    [InlineData("null")]
    public void AMissingOrBrokenFile_GivesTheDefaults_FollowingTheSystem(string? content)
    {
        if (content is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            System.IO.File.WriteAllText(File, content);
        }

        Assert.Equal(new WorkbenchSettings(null, AppTheme.Auto, false), WorkbenchSettings.Load(File));
    }

    [Fact]
    public void AFileThatCannotBeWritten_IsReported()
    {
        Directory.CreateDirectory(File); // a folder where the file should be

        Assert.False(new WorkbenchSettings(AppLanguage.Chinese).Save(File));
        Assert.Equal(new WorkbenchSettings(), WorkbenchSettings.Load(File));
    }

    [Fact]
    public void TheDataRoot_IsEplanMcpHome_WhenSet_AsForTheHost()
    {
        string? before = Environment.GetEnvironmentVariable("EPLANMCP_HOME");
        try
        {
            Environment.SetEnvironmentVariable("EPLANMCP_HOME", _root.FullName);
            Assert.Equal(_root.FullName, WorkbenchDataLocations.FromEnvironment().Root);
            Environment.SetEnvironmentVariable("EPLANMCP_HOME", null);
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp"), WorkbenchDataLocations.FromEnvironment().Root);
        }
        finally
        {
            Environment.SetEnvironmentVariable("EPLANMCP_HOME", before);
        }
    }
}
