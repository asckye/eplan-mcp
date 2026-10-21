using EplanMcp.Host.ClientConfig;

namespace EplanMcp.Host.Tests.ClientConfig;

/// <summary>
/// The two files that depend on what is installed: Claude Desktop's (the MSIX package's copy, spike S17) and VS Code's
/// (Insiders only when it is the only one). Medium: the profile is a temporary directory, never the user's own.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ClientFilePathTests : IDisposable
{
    private readonly string _home = Directory.CreateTempSubdirectory("eplanmcp-profile-").FullName;

    public void Dispose() => Directory.Delete(_home, recursive: true);

    private string AppData => Path.Combine(_home, "AppData", "Roaming");

    private string Packages => Path.Combine(_home, "AppData", "Local", "Packages");

    [Fact]
    public void ClaudeDesktop_WithoutThePackage_IsTheDocumentedFile()
    {
        Directory.CreateDirectory(Path.Combine(Packages, "Microsoft.WindowsTerminal_8wekyb3d8bbwe"));

        Assert.Equal(Path.Combine(AppData, "Claude", "claude_desktop_config.json"), ClaudeDesktop());
    }

    [Theory]
    [InlineData("Claude_pzs8sxrjxfjjc")]
    [InlineData("Claude_0123456789abc")]
    public void ClaudeDesktop_WithThePackage_IsThePackagesCopy(string family)
    {
        Directory.CreateDirectory(Path.Combine(Packages, family));

        Assert.Equal(Path.Combine(Packages, family, "LocalCache", "Roaming", "Claude", "claude_desktop_config.json"), ClaudeDesktop());
    }

    [Fact]
    public void ClaudeDesktop_PrefersTheKnownPackage()
    {
        Directory.CreateDirectory(Path.Combine(Packages, "Claude_0123456789abc"));
        Directory.CreateDirectory(Path.Combine(Packages, McpClient.ClaudePackageFamily));

        Assert.StartsWith(Path.Combine(Packages, McpClient.ClaudePackageFamily) + @"\", ClaudeDesktop(), StringComparison.Ordinal);
    }

    [Fact]
    public void ClaudeDesktop_WithTwoUnknownPackages_IsAmbiguous()
    {
        Directory.CreateDirectory(Path.Combine(Packages, "Claude_0123456789abc"));
        Directory.CreateDirectory(Path.Combine(Packages, "Claude_abcdefghijklm"));

        ClientConfigException refused = Assert.Throws<ClientConfigException>(ClaudeDesktop);
        Assert.Equal(ClientProblem.AmbiguousPackage, refused.Problem);
        Assert.Contains("Claude_0123456789abc, Claude_abcdefghijklm", refused.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Claude_beta")]
    [InlineData("Claude_0123456789abcd")]
    [InlineData("ClaudeHelper_0123456789abc")]
    public void ClaudeDesktop_IgnoresOtherPackageNames(string family)
    {
        Directory.CreateDirectory(Path.Combine(Packages, family));

        Assert.Equal(Path.Combine(AppData, "Claude", "claude_desktop_config.json"), ClaudeDesktop());
    }

    [Theory]
    [InlineData(false, false, "Code")]
    [InlineData(true, false, "Code")]
    [InlineData(true, true, "Code")]
    [InlineData(false, true, "Code - Insiders")]
    public void VsCode_UsesInsidersOnlyWhenItIsTheOnlyOne(bool stable, bool insiders, string folder)
    {
        if (stable)
        {
            Directory.CreateDirectory(Path.Combine(AppData, "Code", "User"));
        }

        if (insiders)
        {
            Directory.CreateDirectory(Path.Combine(AppData, "Code - Insiders", "User"));
        }

        Assert.Equal(Path.Combine(AppData, folder, "User", "mcp.json"), McpClient.Find("vscode-copilot")!.ConfigFile(Folders()));
    }

    private string ClaudeDesktop() => McpClient.Find("claude-desktop")!.ConfigFile(Folders());

    private UserFolders Folders() => new(_home, AppData, Path.Combine(_home, "AppData", "Local"), _ => null);
}
