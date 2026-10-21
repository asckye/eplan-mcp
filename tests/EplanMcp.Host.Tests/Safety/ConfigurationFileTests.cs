using System.Diagnostics;
using EplanMcp.Host.Doctor;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>config.json on disk: doctor reports it, and serve refuses an invalid one (architecture 8.9).</summary>
[Trait("Size", "Medium")]
public sealed class ConfigurationFileTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-config-").FullName);

    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);

    [Fact]
    public void Doctor_WithoutAFile_ReportsTheDefaults()
    {
        DoctorCheck check = DoctorChecks.Configuration(_locations);

        Assert.Equal(DoctorStatus.Ok, check.Status);
        Assert.Contains("(defaults)", check.English, StringComparison.Ordinal);
        Assert.Contains("writes outside scratch: need approval", check.English, StringComparison.Ordinal);
        Assert.Contains("approval deadline 120 s", check.English, StringComparison.Ordinal);
        Assert.Contains("审批期限 120 秒", check.Chinese, StringComparison.Ordinal);
    }

    [Fact]
    public void Doctor_ShowsTheConfiguredApprovalDeadline()
    {
        File.WriteAllText(_locations.ConfigFile, """{"approvalTimeoutSeconds":180}""");

        DoctorCheck check = DoctorChecks.Configuration(_locations);

        Assert.Equal(DoctorStatus.Ok, check.Status);
        Assert.Contains("approval deadline 180 s", check.English, StringComparison.Ordinal);
    }

    [Fact]
    public void Doctor_AnApprovalDeadlineOutOfRange_IsAFailure()
    {
        File.WriteAllText(_locations.ConfigFile, """{"approvalTimeoutSeconds":30}""");

        DoctorCheck check = DoctorChecks.Configuration(_locations);

        Assert.Equal(DoctorStatus.Failure, check.Status);
        Assert.Contains("'approvalTimeoutSeconds' must be a whole number of seconds from 60 to 300", check.English, StringComparison.Ordinal);
    }

    [Fact]
    public void Doctor_AnInvalidFile_IsAFailure()
    {
        File.WriteAllText(_locations.ConfigFile, """{"writesOutsideScratch":"always"}""");

        DoctorCheck check = DoctorChecks.Configuration(_locations);

        Assert.Equal(DoctorStatus.Failure, check.Status);
        Assert.Contains("serve will not start", check.English, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Serve_AnInvalidFile_RefusesToStartWithCode3()
    {
        File.WriteAllText(_locations.ConfigFile, """{"scratchRoots":["relative"]}""");
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "eplanmcp.exe"), "serve")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["EPLANMCP_HOME"] = _locations.Root;

        using Process serve = Process.Start(start)!;
        Task<string> stdout = serve.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        string stderr = await serve.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await serve.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, serve.ExitCode);
        Assert.Contains("config.json", stderr, StringComparison.Ordinal);
        Assert.Contains("full path", stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, await stdout); // stdout belongs to the MCP protocol (CLAUDE.md rule 5)
    }
}
