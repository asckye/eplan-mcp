using EplanMcp.Evals.Server;

namespace EplanMcp.Evals.Tests.Server;

/// <summary><c>claude auth status</c> stood in for by batch files that exit like the real one.</summary>
[Trait("Size", "Medium")]
public sealed class ClaudeLoginTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("eplanmcp-login-");

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public async Task ClaudeLoginProblem_LoggedIn_IsNone()
    {
        Assert.Null(await Preflight.ClaudeLoginProblemAsync(Fake("{\"loggedIn\": true}", exitCode: 0), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ClaudeLoginProblem_LoggedOut_SaysHowToLogIn()
    {
        string? problem = await Preflight.ClaudeLoginProblemAsync(Fake("{\"loggedIn\": false}", exitCode: 1), TestContext.Current.CancellationToken);

        Assert.Equal("claude is not logged in: run `claude auth login` (or `claude setup-token`) once, then start the run again", problem);
    }

    [Fact]
    public async Task ClaudeLoginProblem_AMissingClaude_NamesThePath()
    {
        string missing = Path.Combine(_directory.FullName, "claude.exe");

        string? problem = await Preflight.ClaudeLoginProblemAsync(missing, TestContext.Current.CancellationToken);

        Assert.StartsWith($"could not start claude at {missing}: ", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClaudeLoginProblem_AClaudeThatDoesNotAnswer_IsStopped()
    {
        string path = Path.Combine(_directory.FullName, "claude-hangs.cmd");
        File.WriteAllText(path, "@echo off\r\nping -n 30 127.0.0.1 > nul\r\n");

        string? problem = await Preflight.ClaudeLoginProblemAsync(path, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.Equal("claude auth status did not answer within 1 s", problem);
    }

    private string Fake(string output, int exitCode)
    {
        string path = Path.Combine(_directory.FullName, $"claude-{exitCode}.cmd");
        File.WriteAllText(path, $"@echo off\r\necho {output}\r\nexit /b {exitCode}\r\n");
        return path;
    }
}
