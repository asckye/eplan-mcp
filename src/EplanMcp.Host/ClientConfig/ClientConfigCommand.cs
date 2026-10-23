using System.Globalization;
using EplanMcp.Host.Doctor;

namespace EplanMcp.Host.ClientConfig;

/// <summary>
/// <c>eplanmcp client-config</c> (M5 design 4.11): writes, previews or removes the server <c>eplan</c> in each named
/// client's configuration and reports each client separately; exit code 1 when any failed. Each file is read once and,
/// unless previewing, written in the same run after a backup; every write and removal updates the record. The entry
/// runs the eplanmcp.exe that runs this command. It refuses to run in an MSIX package's environment, where writes to
/// %APPDATA% are redirected (api-notes A6); to write for Claude Code when its plugin provides the server (design 4.7);
/// and to touch Claude Desktop's file while the app runs (spike S17). Never used in serve mode.
/// </summary>
internal sealed class ClientConfigCommand
{
    private readonly UserFolders _folders;
    private readonly ClientRecord _record;
    private readonly LocalServer _server;
    private readonly TimeProvider _time;
    private readonly Func<bool> _claudeDesktopRunning;
    private readonly Func<string?> _msixPackage;
    private readonly string _dataRoot;

    /// <summary>
    /// For tests: a temporary profile and data root, any path for eplanmcp.exe, a fixed clock, and answers for whether
    /// Claude Desktop runs and in which MSIX package's environment this process runs (null: none).
    /// </summary>
    internal ClientConfigCommand(UserFolders folders, HostDataLocations locations, string executable, TimeProvider time, Func<bool> claudeDesktopRunning, Func<string?> msixPackage)
    {
        _folders = folders;
        _dataRoot = locations.Root;
        _record = ClientRecord.For(locations);
        _server = new LocalServer(executable);
        _time = time;
        _claudeDesktopRunning = claudeDesktopRunning;
        _msixPackage = msixPackage;
    }

    public static ClientConfigCommand ForMachine()
    {
        UserFolders folders = UserFolders.FromMachine();
        return new(folders, HostDataLocations.FromEnvironment(), Environment.ProcessPath ?? string.Empty, TimeProvider.System,
            ClaudeDesktopProcess.IsRunning, () => DoctorChecks.MsixPackage(folders.LocalAppData));
    }

    public int Run(ClientConfigRequest request, TextWriter output, CultureInfo culture, bool json)
    {
        ClientConfigReport report = Run(request);
        output.Write(json ? ClientConfigOutput.Json(report) + Environment.NewLine : ClientConfigOutput.Text(report, culture));
        return report.Failed ? 1 : 0;
    }

    internal ClientConfigReport Run(ClientConfigRequest request)
    {
        ClientConfigMode mode = request.Mode;
        if (_msixPackage() is { } package)
        {
            return new(_server, mode, new(ClientProblem.MsixRedirected, package), []);
        }

        if (mode != ClientConfigMode.Remove)
        {
            bool eplanmcp = Path.IsPathFullyQualified(_server.Command) && string.Equals(Path.GetFileName(_server.Command), "eplanmcp.exe", StringComparison.OrdinalIgnoreCase);
            return eplanmcp
                ? new(_server, mode, null, [.. request.Clients.Select(c => Write(c, mode == ClientConfigMode.DryRun, request.Plugin))])
                : new(_server, mode, new(ClientProblem.NotEplanmcp, _server.Command), []);
        }

        if (request.Clients.Count > 0)
        {
            return new(_server, mode, null, [.. request.Clients.Select(c => Remove(c.Name, () => c.ConfigFile(_folders), LocalServer.Name))]);
        }

        try
        {
            return new(_server, mode, null, [.. _record.All().Select(e => Remove(e.Client, () => e.File, e.Entry))]);
        }
        catch (Exception ex) when (ex is ClientConfigException or IOException or UnauthorizedAccessException)
        {
            return new(_server, mode, Issue(ex), []);
        }
    }

    private ClientResult Write(McpClient client, bool dryRun, bool plugin)
    {
        string? file = null;
        try
        {
            if (plugin)
            {
                return ClaudeCodePlugin.Generate(_folders, _dataRoot, _server, _time.GetUtcNow(), dryRun);
            }

            file = client.ConfigFile(_folders);
            Guard(client.Name, file, writing: true);
            ClientFileChange change = ClientFileChange.Read(file);
            ClientFileEdit edit = client.Format == ClientFormat.CodexToml
                ? CodexToml.Write(change.Text, _server)
                : JsonClientFile.Write(change.Text, client, _server);
            ClientOutcome outcome = edit.Text is null ? ClientOutcome.Unchanged : dryRun ? ClientOutcome.WouldWrite : ClientOutcome.Written;
            var result = new ClientResult(client.Name, file, ClientAction.Write, outcome) { Before = edit.Before, After = edit.After, CommentsDropped = edit.CommentsDropped };
            if (dryRun)
            {
                return result;
            }

            result = result with { Backup = edit.Text is null ? null : change.Apply(edit.Text, _time.GetUtcNow()) };
            return Recorded(result, () => _record.Add(new(client.Name, change.FilePath, LocalServer.Name, _time.GetUtcNow())));
        }
        catch (Exception ex) when (ex is ClientConfigException or IOException or UnauthorizedAccessException)
        {
            return new(client.Name, file, ClientAction.Write, ClientOutcome.Failed) { Problem = Issue(ex) };
        }
    }

    private ClientResult Remove(string name, Func<string> locate, string entry)
    {
        string? file = null;
        try
        {
            McpClient client = McpClient.Find(name) ?? throw new ClientConfigException(ClientProblem.UnknownClient, name);
            file = locate();
            Guard(name, file, writing: false);
            ClientFileChange change = ClientFileChange.Read(file);
            ClientFileEdit edit = client.Format == ClientFormat.CodexToml
                ? CodexToml.Remove(change.Text, entry)
                : JsonClientFile.Remove(change.Text, client, entry);
            string? backup = edit.Text is null ? null : change.Apply(edit.Text, _time.GetUtcNow());
            var result = new ClientResult(name, file, ClientAction.Remove, edit.Text is null ? ClientOutcome.Unchanged : ClientOutcome.Removed)
            {
                Before = edit.Before,
                Backup = backup,
                CommentsDropped = edit.CommentsDropped,
            };
            return Recorded(result, () => _record.Remove(name, file, entry));
        }
        catch (Exception ex) when (ex is ClientConfigException or IOException or UnauthorizedAccessException)
        {
            return new(name, file, ClientAction.Remove, ClientOutcome.Failed) { Problem = Issue(ex) };
        }
    }

    /// <summary>The refusals for one client; a preview is refused as the write would be.</summary>
    private void Guard(string client, string file, bool writing)
    {
        if (!Path.IsPathFullyQualified(file))
        {
            // CODEX_HOME or KIMI_CODE_HOME set to a relative path: never write relative to the working directory.
            throw new ClientConfigException(ClientProblem.IoError, $"{file} is not a full path.");
        }

        if (writing && client == "claude-code" && ClaudeCodePlugin.IsInstalled(_folders))
        {
            throw new ClientConfigException(ClientProblem.PluginInstalled, ClaudeCodePlugin.Id);
        }

        if (client == "claude-desktop" && _claudeDesktopRunning())
        {
            throw new ClientConfigException(ClientProblem.ClaudeDesktopRunning, "Claude Desktop");
        }
    }

    /// <summary>The result, with a problem when the record cannot follow what was done to the file.</summary>
    private static ClientResult Recorded(ClientResult result, Action update)
    {
        try
        {
            update();
            return result;
        }
        catch (Exception ex) when (ex is ClientConfigException or IOException or UnauthorizedAccessException)
        {
            return result with { Problem = new(ClientProblem.RecordFailed, Issue(ex).Detail) };
        }
    }

    private static ClientIssue Issue(Exception ex) =>
        ex is ClientConfigException refused ? new(refused.Problem, refused.Detail) : new(ClientProblem.IoError, ex.Message);
}
