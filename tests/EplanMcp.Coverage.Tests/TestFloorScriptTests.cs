using System.Diagnostics;
using System.Globalization;

namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Medium")]
public sealed class TestFloorScriptTests
{
    [Theory]
    [InlineData(90, 100, "Suite", 0, "100 passed, floor 90")]
    [InlineData(80, 100, "Suite", 0, "100 passed, floor 80")]
    [InlineData(79, 100, "Suite", 1, "below 80%")]
    [InlineData(90, 89, "Suite", 1, "below its floor")]
    [InlineData(90, -1, "Suite", 1, "has no test results")]
    [InlineData(90, 100, "Unknown", 1, "Unknown test assemblies")]
    public async Task Floor_UsesThisRunsPassedCountAndKeepsMissingResultsStrict(int floor, int passed, string assembly, int expectedExit, string expectedMessage)
    {
        using Stream script = typeof(TestFloorScriptTests).Assembly.GetManifestResourceStream("Gate.check-test-floor.ps1")!;
        using var reader = new StreamReader(script);
        using var repository = new TempRepository();
        repository.Write("eng/scripts/check-test-floor.ps1", reader.ReadToEnd())
            .Write("tests/test-suites.json", string.Create(CultureInfo.InvariantCulture, $"{{\"suites\":[{{\"assembly\":\"Suite\",\"minPassed\":{floor}}}]}}"))
            .Write("results/.keep", string.Empty);
        if (passed >= 0)
        {
            string results = string.Concat(Enumerable.Repeat("<UnitTestResult outcome=\"Passed\" />", passed));
            repository.Write("results/Suite.trx", $"<TestRun><TestDefinitions><UnitTest><TestMethod codeBase=\"Suite.dll\" /></UnitTest></TestDefinitions><Results>{results}</Results></TestRun>");
        }

        var start = new ProcessStartInfo("pwsh", ["-NoProfile", "-File", Path.Combine(repository.Root, "eng/scripts/check-test-floor.ps1"),
            "-ResultsDirectory", Path.Combine(repository.Root, "results"), "-Assemblies", assembly])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        string messages = await output + await error;
        Assert.Equal(expectedExit, process.ExitCode);
        Assert.Contains(expectedMessage, messages, StringComparison.Ordinal);
    }
}
