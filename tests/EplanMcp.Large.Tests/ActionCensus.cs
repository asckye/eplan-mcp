using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

/// <summary>
/// ADR-0030 maintainer tool: runs each named action once on a copy of a scratch project and records what it did.
/// Names come from the local installation and evidence stays below the scratch root, so neither enters the repository.
/// Every row is written before and after its action, so a crash of EPLAN loses nothing and the next run resumes.
/// </summary>
[Trait("Size", "Large"), Trait("Batch", "ActionCensus")]
public sealed class ActionCensus(LiveEplan eplan)
{
    public static bool Enabled => Environment.GetEnvironmentVariable("EPLANMCP_ACTION_CENSUS") is { Length: > 0 };
    private const int WmClose = 0x0010, WmKeyDown = 0x0100, WmKeyUp = 0x0101, VkEscape = 0x1B;
    private static readonly TimeSpan _closeAfter = TimeSpan.FromSeconds(1.5), _escapeAfter = TimeSpan.FromSeconds(8), _killAfter = TimeSpan.FromSeconds(45);
    private string _project = "", _results = "", _evidence = "", _source = "", _page = "", _settings = "";
    private int _copy;
    private bool _quiet = true;
    private string? _prepare;
    private BridgeClient? _bridge;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact(SkipUnless = nameof(Enabled), Skip = "Set EPLANMCP_ACTION_CENSUS to a census configuration file on the prepared scratch copy.")]
    public async Task RunNames()
    {
        JsonNode config = JsonNode.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("EPLANMCP_ACTION_CENSUS")!))!;
        _evidence = Directory.CreateDirectory((string)config["evidence"]!).FullName;
        _source = (string)config["source"]!;
        _page = (string)config["page"]!;
        string root = LiveEplan.ScratchRoot();
        Assert.True(_evidence.StartsWith(root, StringComparison.OrdinalIgnoreCase) && _source.StartsWith(root, StringComparison.OrdinalIgnoreCase), "Evidence and source stay below the scratch root.");
        string[] names = JsonSerializer.Deserialize<string[]>(File.ReadAllText((string)config["names"]!))!;
        DateTime deadline = DateTime.UtcNow.AddMinutes((double?)config["minutes"] ?? 600);
        _results = Path.Combine(_evidence, "results.jsonl");
        _settings = (string)config["settings"]!;
        // Quiet runs show what works unattended; a run without quiet mode shows which dialog a command opens.
        _quiet = (bool?)config["quiet"] ?? true;
        // Many commands act on the selection, so a later round selects the page's objects first.
        _prepare = (string?)config["prepare"];
        HashSet<string> done = ReadDone(out string? interrupted);
        if (interrupted is not null)
        {
            Append(new JsonObject { ["action"] = interrupted, ["state"] = "interrupted_previous_run" });
            done.Add(interrupted);
        }
        _copy = Directory.Exists(Path.Combine(WriteHost.ScratchFolder, "census")) ? Directory.GetDirectories(Path.Combine(WriteHost.ScratchFolder, "census")).Length : 0;
        _bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        await RawAsync("EplApiModuleAction /register:\"" + OfflineHost.RepositoryFile("tests", "EplanMcp.Large.ConnectionHelper", "EplanMcp.Large.ConnectionHelper.dll") + "\" /Quiet", TimeSpan.FromSeconds(30));
        await RestoreAsync();
        foreach (string name in names.Where(n => !done.Contains(n)))
        {
            if (DateTime.UtcNow > deadline)
            { break; }
            Append(new JsonObject { ["action"] = name, ["state"] = "started" });
            JsonObject row = await RunOneAsync(name);
            Append(row);
            if ((string?)row["state"] == "eplan_gone")
            { return; }
        }
    }

    private async Task<JsonObject> RunOneAsync(string name)
    {
        // The probe refuses any project but the census copy, and a panel or dialog can move EPLAN's current project.
        await eplan.CallAsync("eplan_page_open", new() { ["project"] = _project, ["page"] = _page });
        if (_prepare is not null)
        { await RawAsync(_prepare, TimeSpan.FromSeconds(15)); }
        string before = await HashAsync();
        string settingsBefore = SettingsStamp();
        string result = Path.Combine(_evidence, "last-result.json");
        File.Delete(result);
        var seen = new Dictionary<nint, string>();
        (string output, bool killed, bool escaped) = await WatchAsync("EplanMcpLargeActionCensus /ACTION:" + name + " /RESULT:\"" + result + "\" /QUIET:" + (_quiet ? "1" : "0"), seen);
        var row = new JsonObject { ["action"] = name, ["windows"] = new JsonArray([.. seen.Values.Select(v => (JsonNode)v)]), ["escaped"] = escaped };
        if (File.Exists(result))
        {
            row["probe"] = JsonNode.Parse(File.ReadAllText(result));
            if (row["probe"]!["refused"] is not null)
            { row["state"] = "refused"; return row; }
        }
        else
        { row["remote"] = output.Length > 400 ? output[..400] : output; }
        if (killed || !Alive())
        {

            row["state"] = "eplan_gone";
            row["killed"] = killed;
            return row;
        }
        row["settingsChanged"] = settingsBefore != SettingsStamp();
        if ((bool)row["settingsChanged"]! && (name.Contains("Toggle", StringComparison.Ordinal) || name.Contains("Toogle", StringComparison.Ordinal)))
        {
            // A toggle stores the user's display settings: switch it back at once.
            await WatchAsync("EplanMcpLargeActionCensus /ACTION:" + name + " /RESULT:\"" + result + "\" /QUIET:1", []);
            row["toggledBack"] = true;
        }
        string after;
        try
        { after = await HashAsync(); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            row["state"] = "project_lost";
            row["detail"] = error.Message;
            await RestoreAsync();
            return row;
        }
        row["changed"] = after != before;
        row["state"] = after != before ? "changed" : "unchanged";
        if (after != before)
        {
            await RawAsync("XEsUndoAction", TimeSpan.FromSeconds(30));
            bool restored = await HashAsync() == before;
            row["undoRestores"] = restored;
            if (!restored)
            { await RestoreAsync(); }
        }
        await RawAsync("XGedEscapeAction", TimeSpan.FromSeconds(10));
        return row;
    }

    private async Task<(string Output, bool Killed, bool Escaped)> WatchAsync(string action, Dictionary<nint, string> seen)
    {
        nint main = Process.GetProcessById(eplan.ProcessId).MainWindowHandle;
        HashSet<nint> baseline = Windows();
        using Process remote = StartRemote(action);
        Task<string> output = remote.StandardOutput.ReadToEndAsync(Token);
        var watch = Stopwatch.StartNew();
        TimeSpan nextClose = _closeAfter, nextEscape = _escapeAfter;
        bool escaped = false;
        while (!remote.HasExited)
        {
            await Task.Delay(200, Token);
            if (!Alive())
            { break; }
            nint[] added = [.. Windows().Where(w => !baseline.Contains(w))];
            foreach (nint window in added)
            {
                if (!seen.ContainsKey(window))
                { seen[window] = Describe(window); }
            }
            bool modal = main != 0 && !IsWindowEnabled(main);
            if (modal && added.Length > 0 && watch.Elapsed > nextClose)
            {
                // Cancels the dialog: the census records what it asks for and never answers it.
                foreach (nint window in added)
                { PostMessage(window, WmClose, 0, 0); }
                nextClose = watch.Elapsed + TimeSpan.FromSeconds(3);
            }
            if (watch.Elapsed > nextEscape)
            {
                // Mouse-driven commands wait for a click; Escape ends them as it does for an engineer.
                escaped = true;
                foreach (nint window in new[] { main }.Concat(added))
                {
                    PostMessage(window, WmKeyDown, VkEscape, 0);
                    PostMessage(window, WmKeyUp, VkEscape, 0);
                }
                nextEscape = watch.Elapsed + TimeSpan.FromSeconds(6);
            }
            if (watch.Elapsed > _killAfter)
            {
                Process.GetProcessById(eplan.ProcessId).Kill();
                return ("killed after " + _killAfter, true, escaped);
            }
        }
        return (await output, false, escaped);
    }

    private async Task RestoreAsync()
    {
        await RawAsync("XPrjActionProjectClose", TimeSpan.FromSeconds(30));
        string name = Path.GetFileNameWithoutExtension(_source);
        string folder = Directory.CreateDirectory(Path.Combine(WriteHost.ScratchFolder, "census", (++_copy).ToString(System.Globalization.CultureInfo.InvariantCulture))).FullName;
        File.Copy(_source, Path.Combine(folder, name + ".elk"));
        CopyFolder(Path.ChangeExtension(_source, ".edb"), Path.Combine(folder, name + ".edb"));
        _project = Path.Combine(folder, name);
        await RawAsync("ProjectOpen /Project:\"" + _project + ".elk\" /Quiet", TimeSpan.FromSeconds(60));
        await eplan.CallAsync("eplan_page_open", new() { ["project"] = _project, ["page"] = _page });
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
        { File.Copy(file, Path.Combine(to, Path.GetFileName(file))); }
        foreach (string folder in Directory.GetDirectories(from))
        { CopyFolder(folder, Path.Combine(to, Path.GetFileName(folder))); }
    }

    private async Task<string> HashAsync() => (await LiveSnapshots.TakeAsync(_bridge!, new ProjectSnapshotArgs { Project = _project })).Hash;

    private string SettingsStamp() => string.Join("\n", Directory.EnumerateFiles(_settings, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        .Select(f => f + "\t" + new FileInfo(f).Length + "\t" + File.GetLastWriteTimeUtc(f).Ticks));

    private bool Alive()
    {
        try
        { return !Process.GetProcessById(eplan.ProcessId).HasExited; }
        catch (ArgumentException)
        { return false; }
    }

    private static Process StartRemote(string action)
    {
        string remote = Environment.GetEnvironmentVariable("EPLANMCP_EPLREMOTE") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".local", "tools", "eplremote", "bin", "Release", "net481", "eplremote.exe"));
        var start = new ProcessStartInfo(remote) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("--host");
        start.ArgumentList.Add(Environment.GetEnvironmentVariable("EPLREMOTE_HOST")!);
        start.ArgumentList.Add(action);
        start.Environment["EPLREMOTE_PORT"] = Environment.GetEnvironmentVariable("EPLREMOTE_PORT") ?? "49152";
        return Process.Start(start)!;
    }

    private static async Task RawAsync(string action, TimeSpan timeout)
    {
        using Process remote = StartRemote(action);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(Token);
        limit.CancelAfter(timeout);
        try
        { await remote.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException) when (!Token.IsCancellationRequested)
        { remote.Kill(); }
    }

    private HashSet<string> ReadDone(out string? interrupted)
    {
        var done = new HashSet<string>(StringComparer.Ordinal);
        interrupted = null;
        if (!File.Exists(_results))
        { return done; }
        foreach (string line in File.ReadLines(_results))
        {
            JsonNode row = JsonNode.Parse(line)!;
            string action = (string)row["action"]!;
            if ((string?)row["state"] == "started")
            { interrupted = action; }
            else
            {
                // A refusal means the action never ran, so a later run tries it again.
                if ((string?)row["state"] != "refused" && (string?)row["probe"]?["refused"] is null)
                { done.Add(action); }
                interrupted = null;
            }
        }
        return done;
    }

    private void Append(JsonObject row) => File.AppendAllText(_results, row.ToJsonString() + Environment.NewLine);

    private HashSet<nint> Windows()
    {
        var found = new HashSet<nint>();
        EnumWindows((window, parameter) =>
        {
            _ = GetWindowThreadProcessId(window, out uint owner);
            if (owner == (uint)eplan.ProcessId && IsWindowVisible(window))
            { found.Add(window); }
            return true;
        }, 0);
        return found;
    }

    private static string Describe(nint window)
    {
        var parts = new List<string> { Text(window) };
        EnumChildWindows(window, (child, _) =>
        {
            string text = Text(child);
            if (text.Length > 0 && parts.Count < 12)
            { parts.Add(text); }
            return true;
        }, 0);
        return string.Join(" | ", parts);
    }

    private static string Text(nint window)
    {
        char[] buffer = new char[200];
        int length = GetWindowText(window, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : "";
    }

    private delegate bool EnumProc(nint window, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint parent, EnumProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint process);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, [Out] char[] text, int size);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint window, int message, nint first, nint second);
}
