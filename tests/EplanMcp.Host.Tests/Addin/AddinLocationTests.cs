using EplanMcp.Host.Addin;

namespace EplanMcp.Host.Tests.Addin;

/// <summary>Which add-in DLL install-addin registers: the installed layout, else the build of the same configuration. Medium.</summary>
[Trait("Size", "Medium")]
public sealed class AddinLocationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-addin-location-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Find_InAnInstalledApp_IsTheDllUnderBridgeAndUnblocksAllOfBridge()
    {
        string app = Path.Combine(_root, "EplanMcp", "app");
        string dll = Put(app, "bridge", "net481", "EplanMcp.Bridge.Eplan.dll");

        Assert.Equal(new AddinLocation(dll, Path.Combine(app, "bridge")), AddinLocation.Find(app, out _));
    }

    [Theory]
    [InlineData("Release")]
    [InlineData("Debug")]
    public void Find_InADevelopmentBuild_IsTheAddinOfTheSameConfiguration(string configuration)
    {
        string src = Path.Combine(_root, "repo", "src");
        string host = Path.Combine(src, "EplanMcp.Host", "bin", configuration, "net10.0");
        Directory.CreateDirectory(host);
        string dll = Put(src, "EplanMcp.Bridge.Eplan", "bin", configuration, "net481", "EplanMcp.Bridge.Eplan.dll");
        Put(src, "EplanMcp.Bridge.Eplan", "bin", configuration == "Debug" ? "Release" : "Debug", "net481", "EplanMcp.Bridge.Eplan.dll");

        Assert.Equal(new AddinLocation(dll, Path.GetDirectoryName(dll)!), AddinLocation.Find(host + Path.DirectorySeparatorChar, out _));
    }

    [Fact]
    public void Find_WithoutTheBuildOfTheSameConfiguration_IsNullAndSaysWhereItLooked()
    {
        string src = Path.Combine(_root, "repo", "src");
        string host = Path.Combine(src, "EplanMcp.Host", "bin", "Debug", "net10.0");
        Directory.CreateDirectory(host);
        Put(src, "EplanMcp.Bridge.Eplan", "bin", "Release", "net481", "EplanMcp.Bridge.Eplan.dll");

        Assert.Null(AddinLocation.Find(host, out IReadOnlyList<string> searched));
        Assert.Equal(
            [Path.Combine(host, "bridge", "net481", "EplanMcp.Bridge.Eplan.dll"), Path.Combine(src, "EplanMcp.Bridge.Eplan", "bin", "Debug", "net481", "EplanMcp.Bridge.Eplan.dll")],
            searched);
    }

    [Fact]
    public void Find_OutsideBothLayouts_LooksOnlyNextToTheHost()
    {
        string other = Path.Combine(_root, "tools", "bin", "Release", "net10.0");
        Directory.CreateDirectory(other);

        Assert.Null(AddinLocation.Find(other, out IReadOnlyList<string> searched));
        Assert.Equal([Path.Combine(other, "bridge", "net481", "EplanMcp.Bridge.Eplan.dll")], searched);
    }

    private static string Put(params string[] parts)
    {
        string file = Path.Combine(parts);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "dll");
        return file;
    }
}
