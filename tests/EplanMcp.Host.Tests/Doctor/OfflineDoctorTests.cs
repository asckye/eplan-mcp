using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace EplanMcp.Host.Tests.Doctor;

[Trait("Size", "Medium")]
public sealed class OfflineDoctorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OfflineDoctor_WithoutBridge_SkipsLiveChecksButStillFailsInvalidConfiguration(bool invalidConfig)
    {
        string home = Directory.CreateTempSubdirectory("eplanmcp-offline-doctor-").FullName;
        try
        {
            if (invalidConfig)
            {
                File.WriteAllText(Path.Combine(home, "config.json"), "{");
            }
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "eplanmcp.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            foreach (string argument in new[] { "doctor", "--offline", "--format", "json" })
            {
                start.ArgumentList.Add(argument);
            }
            start.Environment["EPLANMCP_HOME"] = home;
            start.Environment["EPLANMCP_PIPE_PREFIX"] = "EplanMcp.OfflineDoctor." + Guid.NewGuid().ToString("N");
            using Process process = Process.Start(start)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            Task<string> errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(invalidConfig ? 1 : 0, process.ExitCode);
            Assert.Empty(await errors);
            using JsonDocument json = JsonDocument.Parse(await output);
            JsonElement[] checks = json.RootElement.GetProperty("checks").EnumerateArray().ToArray();
            Assert.Equal("info", Assert.Single(checks, check => check.GetProperty("id").GetString() == "bridge.pipe").GetProperty("status").GetString());
            Assert.DoesNotContain(checks, check => check.GetProperty("id").GetString() is "bridge.handshake" or "bridge.licence");
            if (invalidConfig)
            {
                Assert.Equal("failure", Assert.Single(checks, check => check.GetProperty("id").GetString() == "config").GetProperty("status").GetString());
            }
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }
}
