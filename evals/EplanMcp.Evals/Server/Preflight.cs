using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Evals.Scoring;

namespace EplanMcp.Evals.Server;

/// <summary>
/// The checks before a run (agent evals design 4.2, step 1). Each returns why the run must not start, or null. The
/// evals only read, but still run on a scratch copy only, in the spirit of CLAUDE.md rule 1.
/// </summary>
internal static class Preflight
{
    public static readonly TimeSpan LoginTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The scratch root of ADR-0008: <c>%USERPROFILE%\EplanMcp\scratch</c>, or under <c>EPLANMCP_HOME</c>.</summary>
    public static string ScratchRoot() => Path.Combine(
        Environment.GetEnvironmentVariable("EPLANMCP_HOME") is { Length: > 0 } home ? home : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp"),
        "scratch");

    /// <summary><c>claude auth status</c> exits with 0 only when logged in; it gets <see cref="LoginTimeout"/> to answer.</summary>
    public static Task<string?> ClaudeLoginProblemAsync(string claude, CancellationToken cancellationToken) =>
        ClaudeLoginProblemAsync(claude, LoginTimeout, cancellationToken);

    internal static async Task<string?> ClaudeLoginProblemAsync(string claude, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(claude) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("auth");
        start.ArgumentList.Add("status");
        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {claude}.");
        }
        catch (Win32Exception ex)
        {
            return $"could not start claude at {claude}: {ex.Message}";
        }

        using (process)
        using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            limit.CancelAfter(timeout);
            try
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync(limit.Token);
                Task<string> errors = process.StandardError.ReadToEndAsync(limit.Token);
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
                await Task.WhenAll(output, errors).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                cancellationToken.ThrowIfCancellationRequested();
                return $"claude auth status did not answer within {timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} s";
            }

            return process.ExitCode == 0 ? null : "claude is not logged in: run `claude auth login` (or `claude setup-token`) once, then start the run again";
        }
    }

    /// <summary>EPLAN answers through eplanmcp, and its current project lies under <paramref name="scratchRoot"/>.</summary>
    public static async Task<string?> EplanProblemAsync(IToolClient client, string scratchRoot, CancellationToken cancellationToken)
    {
        ToolOutcome status = await client.CallAsync("eplan_session_status", [], cancellationToken).ConfigureAwait(false);
        if (status.IsError || !IsTrue(status.Structured?["connected"]))
        {
            string hint = Comparison.Text(status.Structured?["hint"]) ?? status.Text;
            return $"EPLAN is not connected: {hint}";
        }

        PagedOutcome projects = await ToolPages.AllAsync(client, "eplan_project_list", new JsonObject { ["limit"] = 200 }, cancellationToken).ConfigureAwait(false);
        if (projects.Problem is { } problem)
        {
            return problem;
        }

        JsonObject? current = projects.Pages.SelectMany(p => p["projects"] as JsonArray ?? []).OfType<JsonObject>()
            .FirstOrDefault(p => IsTrue(p["current"]));
        return current is null
            ? "no project is current in EPLAN: open the fixture project from the scratch root"
            : ScratchProblem(Comparison.Text(current["path"]) ?? string.Empty, scratchRoot);
    }

    /// <summary>Whether <paramref name="projectPath"/> (the .elk path without the extension) is not inside the scratch root.</summary>
    public static string? ScratchProblem(string projectPath, string scratchRoot)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scratchRoot)) + Path.DirectorySeparatorChar;
        bool inside = projectPath.Length > 0 && Path.GetFullPath(projectPath).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        return inside ? null : $"the current project {projectPath} is not under the scratch root {root}: the evals run only on a scratch copy of the fixture";
    }

    private static bool IsTrue(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.True;
}
