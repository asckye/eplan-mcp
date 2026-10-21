using EplanMcp.Host.ClientConfig;

namespace EplanMcp.Host.Tests.ClientConfig;

/// <summary>
/// The read-only check for the Claude Code plugin, on the file formats spike S17 recorded. Medium: the settings are in
/// a temporary profile, never the user's own.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ClaudeCodePluginTests : IDisposable
{
    private readonly string _home = Directory.CreateTempSubdirectory("eplanmcp-profile-").FullName;

    private string Settings => Path.Combine(_home, ".claude", "settings.json");

    private string Installed => Path.Combine(_home, ".claude", "plugins", "installed_plugins.json");

    public void Dispose() => Directory.Delete(_home, recursive: true);

    [Fact]
    public void IsInstalled_WithoutClaudeCodesFiles_IsFalse()
    {
        Assert.False(IsInstalled());
    }

    [Theory]
    [InlineData("""{"enabledPlugins": {"eplan-mcp@eplanmcp-local": true}}""", true)]
    [InlineData("""{"enabledPlugins": {"eplan-mcp@eplanmcp-local": false}}""", true)]
    [InlineData("""{"enabledPlugins": {"other@market": true}, "env": {"eplan-mcp@eplanmcp-local": "1"}}""", false)]
    [InlineData("""{"enabledPlugins": ["eplan-mcp@eplanmcp-local"]}""", false)]
    [InlineData("""
        // user settings
        {"enabledPlugins": {"eplan-mcp@eplanmcp-local": true,},}
        """, true)]
    public void IsInstalled_ReadsEnabledPluginsInTheUserSettings(string settings, bool installed)
    {
        Write(Settings, settings);

        Assert.Equal(installed, IsInstalled());
    }

    [Theory]
    [InlineData("""{"version": 2, "plugins": {"eplan-mcp@eplanmcp-local": [{"scope": "user", "version": "0.1.0"}]}}""", true)]
    [InlineData("""{"version": 2, "plugins": {"eplan-mcp@other-market": []}}""", false)]
    [InlineData("""{"version": 2, "plugins": {}}""", false)]
    public void IsInstalled_ReadsTheInstalledPlugins(string plugins, bool installed)
    {
        Write(Settings, "{}");
        Write(Installed, plugins);

        Assert.Equal(installed, IsInstalled());
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"enabledPlugins": {"a": true, "a": false}}""")]
    public void IsInstalled_WhenTheSettingsCannotBeRead_RefusesRatherThanGuesses(string settings)
    {
        Write(Settings, settings);

        ClientConfigException refused = Assert.Throws<ClientConfigException>(() => IsInstalled());
        Assert.Equal(ClientProblem.InvalidFile, refused.Problem);
        Assert.Contains(Settings, refused.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void IsInstalled_ChangesNothing()
    {
        Write(Settings, """{"enabledPlugins": {"eplan-mcp@eplanmcp-local": true}}""");
        Write(Installed, """{"version": 2, "plugins": {}}""");
        DateTime[] times = [File.GetLastWriteTimeUtc(Settings), File.GetLastWriteTimeUtc(Installed)];

        IsInstalled();

        Assert.Equal(times, new[] { File.GetLastWriteTimeUtc(Settings), File.GetLastWriteTimeUtc(Installed) });
        Assert.Equal(2, Directory.GetFiles(_home, "*", SearchOption.AllDirectories).Length);
    }

    private bool IsInstalled() => ClaudeCodePlugin.IsInstalled(new UserFolders(_home, _home, _home, _ => null));

    private static void Write(string file, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }
}
