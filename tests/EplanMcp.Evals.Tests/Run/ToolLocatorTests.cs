using EplanMcp.Evals.Run;

namespace EplanMcp.Evals.Tests.Run;

[Trait("Size", "Medium")]
public sealed class ToolLocatorTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("eplanmcp-locate-");

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public void Claude_OnThePath_ComesFirst()
    {
        string onPath = Touch("bin", "claude.exe");
        Touch("appdata", "Claude", "claude-code", "2.1.299", "f058", "claude.exe");

        Assert.Equal(onPath, ToolLocator.Claude($"{Path.Combine(_directory.FullName, "empty")};{Path.GetDirectoryName(onPath)}", Path.Combine(_directory.FullName, "appdata"), Path.Combine(_directory.FullName, "local")));
    }

    [Fact]
    public void Claude_FromTheDesktopApp_IsTheNewestVersion()
    {
        Touch("appdata", "Claude", "claude-code", "2.1.295", "d2e2", "claude.exe");
        string newest = Touch("appdata", "Claude", "claude-code", "2.1.299", "f058", "claude.exe");
        Touch("appdata", "Claude", "claude-code", "latest", "x", "claude.exe");
        Touch("appdata", "Claude", "claude-code", "2.1.1000", "empty.txt");

        Assert.Equal(newest, ToolLocator.Claude(null, Path.Combine(_directory.FullName, "appdata"), Path.Combine(_directory.FullName, "local")));
    }

    [Fact]
    public void Claude_OutsideThePackage_IsFoundInItsRedirectedCopy()
    {
        // A terminal outside the MSIX package sees no Claude folder in %APPDATA% (api-notes A6).
        Touch("local", "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Roaming", "Claude", "claude-code", "2.1.295", "d2e2", "claude.exe");
        string newest = Touch("local", "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Roaming", "Claude", "claude-code", "2.1.299", "f058", "claude.exe");
        Touch("local", "Packages", "Other_abc", "LocalCache", "Roaming", "Claude", "claude-code", "9.9.9", "x", "claude.exe");

        Assert.Equal(newest, ToolLocator.Claude(null, Path.Combine(_directory.FullName, "appdata"), Path.Combine(_directory.FullName, "local")));
    }

    [Fact]
    public void Claude_InTheAppsLocalData_IsFoundThere()
    {
        // Desktop app 3.6556 keeps its Claude Code under %LOCALAPPDATA%\Claude-Data\claude_<publisher>\claude-code.
        Touch("local", "Claude-Data", "claude_pzs8sxrjxfjjc", "claude-code", "2.1.299", "f058", "claude.exe");
        string newest = Touch("local", "Claude-Data", "claude_pzs8sxrjxfjjc", "claude-code", "2.1.308", "c17a", "claude.exe");

        Assert.Equal(newest, ToolLocator.Claude(null, Path.Combine(_directory.FullName, "appdata"), Path.Combine(_directory.FullName, "local")));
    }

    [Fact]
    public void Claude_Nowhere_IsNull()
    {
        Assert.Null(ToolLocator.Claude(string.Empty, Path.Combine(_directory.FullName, "appdata"), Path.Combine(_directory.FullName, "local")));
    }

    [Fact]
    public void Eplanmcp_PrefersTheReleaseBuild()
    {
        File.WriteAllText(Path.Combine(_directory.FullName, "EplanMcp.slnx"), "<Solution />");
        Touch("src", "EplanMcp.Host", "bin", "Debug", "net10.0", "eplanmcp.exe");
        string release = Touch("src", "EplanMcp.Host", "bin", "Release", "net10.0", "eplanmcp.exe");

        string? root = ToolLocator.RepositoryRoot(Directory.CreateDirectory(Path.Combine(_directory.FullName, "evals", "tasks")).FullName);

        Assert.Equal(_directory.FullName, root);
        Assert.Equal(release, ToolLocator.Eplanmcp(root));
        Assert.Null(ToolLocator.Eplanmcp(null));
    }

    private string Touch(params string[] parts)
    {
        string path = Path.Combine([_directory.FullName, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return path;
    }
}
