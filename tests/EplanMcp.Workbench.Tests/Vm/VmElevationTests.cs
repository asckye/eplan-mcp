using EplanMcp.Workbench.Vm;

namespace EplanMcp.Workbench.Tests.Vm;

[Trait("Size", "Small")]
public sealed class VmElevationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Elevation_UsesAbsoluteHostRunasAndValidatedArgumentListOnly(bool remove)
    {
        const string executable = @"C:\VM tools\eplanmcp.exe";
        var start = VmElevation.StartInfo(executable, remove, "8766");
        Assert.Equal(executable, start.FileName);
        Assert.True(start.UseShellExecute);
        Assert.Equal("runas", start.Verb);
        Assert.Equal(["vm-firewall", remove ? "remove" : "add", "--port", "8766"], start.ArgumentList);
        Assert.Empty(start.Arguments);
        Assert.False(start.RedirectStandardOutput);
        Assert.False(start.RedirectStandardError);
    }

    [Theory]
    [InlineData("eplanmcp.exe", "8766")]
    [InlineData(@"C:\cmd.exe", "8766")]
    [InlineData(@"C:\dotnet.exe", "8766")]
    [InlineData(@"C:\eplanmcp.exe", "0")]
    [InlineData(@"C:\eplanmcp.exe", "8766;whoami")]
    [InlineData(@"C:\eplanmcp.exe", "8766 & whoami")]
    public void Elevation_RejectsUnvalidatedValuesBeforeProcessCreation(string executable, string port) =>
        Assert.Throws<ArgumentException>(() => VmElevation.StartInfo(executable, false, port));
}
