using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Doctor;

namespace EplanMcp.Host.Tests.EndToEnd;

/// <summary>
/// The real <c>eplanmcp.exe</c> with <c>--format</c>: JSON is one UTF-8 object on stdout whatever the console's code
/// page, a wrong format is a wrong command line. The data directory has a Chinese name to show the encoding; doctor
/// looks for add-ins under a test-only pipe prefix, so it never reaches a running EPLAN.
/// </summary>
[Trait("Size", "Medium")]
public sealed class CliEndToEndTests : IDisposable
{
    private readonly string _home = Directory.CreateTempSubdirectory("eplanmcp-数据-").FullName;

    public void Dispose() => Directory.Delete(_home, recursive: true);

    [Theory]
    [InlineData("doctor --format")]
    [InlineData("doctor --format xml")]
    [InlineData("audit verify --format")]
    [InlineData("audit verify --format text")]
    public async Task WrongFormat_ExitsWith2AndPrintsTheUsageOnStderr(string arguments)
    {
        (int exit, string stdout, string stderr) = await Run(arguments);

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, stdout);
        Assert.Equal(CliArguments.Usage, stderr.TrimEnd());
    }

    [Fact]
    public async Task AuditVerifyJson_IsOneUtf8Object()
    {
        (int exit, string stdout, _) = await Run("audit verify --format json");

        Assert.Equal(0, exit);
        JsonNode json = JsonNode.Parse(stdout)!;
        Assert.Equal(Path.Combine(_home, "audit"), json["directory"]!.GetValue<string>());
        Assert.False(json["exists"]!.GetValue<bool>());
        Assert.True(json["intact"]!.GetValue<bool>());
    }

    [Fact]
    public async Task DoctorJson_ListsEveryCheckWithAKnownIdAndExitsAsTheText()
    {
        (int exit, string stdout, _) = await Run("doctor --format json");

        JsonObject json = JsonNode.Parse(stdout)!.AsObject();
        string[] ids = [.. typeof(DoctorCheckIds).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (string)f.GetRawConstantValue()!)];
        JsonObject[] checks = [.. json["checks"]!.AsArray().Select(c => c!.AsObject())];
        Assert.Equal(1, json["schemaVersion"]!.GetValue<int>());
        Assert.Equal("host.version", checks[0]["id"]!.GetValue<string>());
        Assert.All(checks, c => Assert.Contains(c["id"]!.GetValue<string>(), ids));
        Assert.Contains(checks, c => c["zh"]!.GetValue<string>().Contains(_home, StringComparison.Ordinal));
        // No add-in answers on the test prefix, so the Bridge check fails, and with it doctor.
        Assert.Contains(checks, c => c["id"]!.GetValue<string>() == "bridge.pipe" && c["status"]!.GetValue<string>() == "failure");
        Assert.Equal(("failure", 1), (json["status"]!.GetValue<string>(), exit));
    }

    private async Task<(int Exit, string Stdout, string Stderr)> Run(string arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "eplanmcp.exe"), arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["EPLANMCP_HOME"] = _home;
        start.Environment[PipeBridgeDiscovery.PrefixVariable] = "EplanMcpCli." + Guid.NewGuid().ToString("N") + ".";

        using Process process = Process.Start(start)!;
        using var stdout = new MemoryStream();
        Task copy = process.StandardOutput.BaseStream.CopyToAsync(stdout, TestContext.Current.CancellationToken);
        string stderr = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await copy;
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        // Strict UTF-8 without a byte order mark: anything else fails here rather than in the Workbench.
        byte[] bytes = stdout.ToArray();
        Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..], "stdout starts with a UTF-8 byte order mark");
        return (process.ExitCode, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes), stderr);
    }
}
