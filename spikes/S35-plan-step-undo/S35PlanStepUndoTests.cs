using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>
/// Spike S35 part 1 (M6 phase 2 design, section 9 U1), run once by hand: compiled into EplanMcp.Large.Tests only for the
/// run (it is not part of the suite). On the write copy through <see cref="DestructiveHost"/>: a sequence of separate
/// tool calls covering the phase-2 step operations, then EPLAN's own undo action (<c>XEsUndoAction</c> through the
/// remote client, as S1, S2 and S10 did) one step at a time, reading the state back after each; the side effects of the
/// Destructive tools' backup; and whether a macro file held open for reading by another process can still be inserted.
/// Every write is on new pages of <see cref="DestructiveHost.Prefix"/> of the copy; approvals come from the test's
/// scripted channel, which grants only the change lists registered here.
/// </summary>
[Trait("Size", "Large")]
[Trait("Spike", "S35")]
public sealed class S35PlanStepUndoTests : IClassFixture<DestructiveHost>
{
    /// <summary>The remote client of .local/tools/eplremote (not committed), built in Release, beside this repository's build.</summary>
    private static readonly string _remote = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".local", "tools", "eplremote", "bin", "Release", "net481", "eplremote.exe"));

    /// <summary>The full path of <see cref="MacroTests.FeedbackControl"/> under this machine's $(MD_MACROS).</summary>
    private static string MacroFile => Environment.GetEnvironmentVariable("S35_MACRO_FILE") ?? throw new InvalidOperationException("Set S35_MACRO_FILE to the full path of " + MacroTests.FeedbackControl + ".");

    private readonly DestructiveHost _host;

    public S35PlanStepUndoTests(LiveEplan eplan, DestructiveHost host)
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        host.Approvals.Forget();
        _host = host;
    }

    private static void Log(string line)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(line);
        if (Environment.GetEnvironmentVariable("S35_LOG") is { Length: > 0 } file)
        {
            File.AppendAllText(file, DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + line + Environment.NewLine);
        }
    }

    [Fact]
    public async Task UndoGranularity_BackupSideEffects_AndMacroReadHandleAsync()
    {
        await MakeWritableCurrentAsync();
        JsonElement projects = await _host.CallAsync("eplan_project_list", []);
        Log("open projects: " + string.Join(", ", projects.GetProperty("projects").EnumerateArray().Select(p => $"{p.GetProperty("name").GetString()}{(p.GetProperty("current").GetBoolean() ? " (current)" : string.Empty)}")));

        var states = new List<(string Step, string State)>();
        string[] p0 = await _host.NewPagesAsync(0 + 1);
        int first = DestructiveHost.Number(p0[0]);
        states.Add(("s0 page.create P0 (earlier history)", await StateAsync(first, null)));

        string[] pair = await _host.NewPagesAsync(2);
        string p1 = pair[0], p2 = pair[1];
        states.Add(("s1 page.create P1,P2", await StateAsync(first, p1)));

        await _host.CallAsync("eplan_page_update", new() { ["project"] = _host.Writable, ["pages"] = DestructiveHost.Json(new[] { new { name = p1, description = "S35 step 2" } }) });
        states.Add(("s2 page.update P1", await StateAsync(first, p1)));

        JsonElement inserted = await _host.InsertMacroAsync(p1);
        Log("macro.insert: " + inserted.GetProperty("execution").GetString() + ", devices " + string.Join(" ", inserted.GetProperty("devices").EnumerateArray().Select(d => d.GetProperty("tag").GetString())));
        states.Add(("s3 macro.insert P1", await StateAsync(first, p1)));

        string tag = inserted.GetProperty("devices").EnumerateArray().Select(d => d.GetProperty("tag").GetString()!).First(t => !t.Contains('?', StringComparison.Ordinal) && t.Contains('-', StringComparison.Ordinal));
        JsonElement updated = await _host.CallAsync("eplan_device_update", new() { ["project"] = _host.Writable, ["tag"] = tag, ["new_tag"] = tag + "9" });
        Log($"device.update {tag} -> {tag}9: {updated.GetProperty("execution").GetString()}");
        states.Add(("s4 device.update", await StateAsync(first, p1)));

        Dictionary<string, object?> renumberDevices = new() { ["project"] = _host.Writable, ["pages"] = DestructiveHost.Json(new[] { p1 }) };
        int devices = (await _host.CallAsync("eplan_device_renumber", new(renumberDevices) { ["dry_run"] = true })).GetProperty("scope").GetProperty("devices").GetInt32();
        _host.Approvals.Approve("eplan_device_renumber", _host.Writable, [$"renumber device tags containing '?' on 1 page ({devices} {(devices == 1 ? "device" : "devices")}), scheme: EPLAN default, start 1, step 1"]);
        JsonElement numbered = await DestructiveAsync("eplan_device_renumber", renumberDevices);
        Log($"device.renumber: {numbered.GetProperty("execution").GetString()}, changed {numbered.GetProperty("changedCount").GetInt32()}, undo {numbered.GetProperty("undo")}");
        states.Add(("s5 device.renumber P1", await StateAsync(first, p1)));

        int target = DestructiveHost.Number(p2) + 150;
        Dictionary<string, object?> renumberPage = new() { ["project"] = _host.Writable, ["pages"] = DestructiveHost.Json(new[] { p2 }), ["start"] = target, ["step"] = 1 };
        _host.Approvals.Approve("eplan_page_renumber", _host.Writable, ["renumber page " + p2]);
        JsonElement renumbered = await DestructiveAsync("eplan_page_renumber", renumberPage);
        string p2New = renumbered.GetProperty("changed").EnumerateArray().Single().GetProperty("to").GetString()!;
        Log($"page.renumber {p2} -> {p2New}: {renumbered.GetProperty("execution").GetString()}");
        states.Add(("s6 page.renumber P2", await StateAsync(first, p1)));

        Dictionary<string, object?> delete = new() { ["project"] = _host.Writable, ["pages"] = DestructiveHost.Json(new[] { p2New }) };
        JsonElement dryRun = await _host.CallAsync("eplan_page_delete", new(delete) { ["dry_run"] = true });
        _host.Approvals.Approve("eplan_page_delete", _host.Writable, dryRun.GetProperty("pages").EnumerateArray().Select(DestructiveHost.DeleteLine));
        JsonElement deleted = await DestructiveAsync("eplan_page_delete", delete);
        Log($"page.delete {p2New}: {deleted.GetProperty("execution").GetString()}");
        states.Add(("s7 page.delete P2'", await StateAsync(first, p1)));

        for (int i = 0; i < states.Count; i++)
        {
            Log($"state {i} after {states[i].Step}: {states[i].State}");
        }

        // EPLAN's undo, one action at a time; which recorded state does the copy match afterwards?
        int undoable = Enumerable.Range(1, states.Count - 1).Count(i => states[i].State != states[i - 1].State) + 1;
        Log($"committed steps that changed the copy: {undoable}; undoing {undoable + 1} times");
        for (int k = 1; k <= undoable + 1; k++)
        {
            var clock = Stopwatch.StartNew();
            string answer = Action("XEsUndoAction");
            try
            {
                string now = await StateAsync(first, p1);
                int[] matches = [.. Enumerable.Range(0, states.Count).Where(i => states[i].State == now)];
                Log($"undo {k}: {answer} ({clock.ElapsedMilliseconds} ms); matches state {(matches.Length == 0 ? "none: " + now : string.Join("/", matches))}");
            }
            catch (Exception ex) when (ex is InvalidOperationException or Xunit.Sdk.XunitException)
            {
                Log($"undo {k}: {answer}; reading back failed, stopping: {ex.Message[..Math.Min(ex.Message.Length, 300)]}");
                return;
            }
        }

        await MacroReadHandleAsync();
        await CleanUpAsync(first);
    }

    /// <summary>A write that EPLAN rolls back (the same macro at the same point again, MacroInsertTests): does it leave an undo step?</summary>
    [Fact]
    public async Task ARolledBackWrite_LeavesNoUndoStepAsync()
    {
        await MakeWritableCurrentAsync();
        string page = (await _host.NewPagesAsync(1))[0];
        int first = DestructiveHost.Number(page);
        string created = await StateAsync(first, page);
        JsonElement inserted = await _host.InsertMacroAsync(page);
        string withMacro = await StateAsync(first, page);
        JsonElement again = await _host.ErrorAsync("eplan_macro_insert", new()
        {
            ["project"] = _host.Writable,
            ["page"] = page,
            ["macro"] = MacroTests.FeedbackControl,
            ["x"] = 40,
            ["y"] = 280,
            ["numbering"] = "number",
        });
        Log($"insert: {inserted.GetProperty("execution").GetString()}; same point again: {again.GetProperty("reason").GetString()} {again.GetProperty("execution").GetString()}; copy unchanged by it: {await StateAsync(first, page) == withMacro}");
        Log($"undo 1: {Action("XEsUndoAction")}; state is {Name(await StateAsync(first, page))}");
        Log($"undo 2: {Action("XEsUndoAction")}; state is {Name(await StateAsync(first, page))}");
        await CleanUpAsync(first);

        string Name(string state) => state == withMacro ? "with the macro" : state == created ? "the new empty page" : state.Length == 0 ? "before the page" : "other: " + state;
    }

    private async Task MakeWritableCurrentAsync()
    {
        // EPLAN's undo works on the current project, which the Large tests need to be the fixture when they start; close the
        // projects before the write copy one by one (XPrjActionProjectClose closes the current one, api-notes) until it is current.
        for (int i = 0; i < 3 && await CurrentAsync() is { } current && !string.Equals(current, _host.Writable, StringComparison.OrdinalIgnoreCase); i++)
        {
            Log($"closing the current project {current}: {Action("XPrjActionProjectClose")}");
        }

        Assert.Equal(_host.Writable, await CurrentAsync(), ignoreCase: true);
    }

    /// <summary>A Destructive call with the open projects and the page count before and after, and its duration.</summary>
    private async Task<JsonElement> DestructiveAsync(string tool, Dictionary<string, object?> args)
    {
        string before = await SessionAsync();
        var clock = Stopwatch.StartNew();
        JsonElement result = await _host.CallAsync(tool, args);
        clock.Stop();
        string after = await SessionAsync();
        Log($"{tool}: {clock.ElapsedMilliseconds} ms, backup {result.GetProperty("backup")}; session {(before == after ? "unchanged" : "CHANGED: " + before + " => " + after)}");
        return result;
    }

    private async Task<string> SessionAsync()
    {
        JsonElement status = await _host.CallAsync("eplan_session_status", []);
        JsonElement list = await _host.CallAsync("eplan_project_list", []);
        JsonElement project = await _host.CallAsync("eplan_project_get", new() { ["project"] = _host.Writable });
        return string.Join("; ",
            "eplan " + status.GetProperty("eplan").GetProperty("processId").GetInt32().ToString(CultureInfo.InvariantCulture),
            string.Join(",", list.GetProperty("projects").EnumerateArray().Select(p => p.GetProperty("path").GetString() + (p.GetProperty("current").GetBoolean() ? "*" : string.Empty) + (p.GetProperty("readOnly").GetBoolean() ? "(ro)" : string.Empty))),
            "pages " + project.GetProperty("counts").GetProperty("pages").GetInt32().ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>The test's pages (number from <paramref name="first"/> on) with their descriptions, and the devices on <paramref name="page"/>.</summary>
    private async Task<string> StateAsync(int first, string? page)
    {
        JsonElement list = await _host.CallAsync("eplan_page_list", new() { ["project"] = _host.Writable, ["name_prefix"] = DestructiveHost.Prefix, ["limit"] = 200 });
        var text = new StringBuilder();
        foreach (JsonElement p in list.GetProperty("pages").EnumerateArray().Where(p => DestructiveHost.Number(p.GetProperty("name").GetString()!) >= first))
        {
            text.Append(p.GetProperty("name").GetString()![DestructiveHost.Prefix.Length..]).Append('[').Append(p.GetProperty("description").GetString()).Append("] ");
        }

        if (page is not null && await _host.HasPageAsync(page))
        {
            JsonElement get = await _host.CallAsync("eplan_page_get", new() { ["project"] = _host.Writable, ["page"] = page });
            text.Append("| ").Append(string.Join(" ", get.GetProperty("devices").EnumerateArray().Select(d => d.GetProperty("tag").GetString()).Order(StringComparer.Ordinal)));
        }

        return text.ToString().Trim();
    }

    private async Task<string?> CurrentAsync()
    {
        JsonElement list = await _host.CallAsync("eplan_project_list", []);
        return list.GetProperty("projects").EnumerateArray().Where(p => p.GetProperty("current").GetBoolean()).Select(p => p.GetProperty("path").GetString()).FirstOrDefault();
    }

    private static string Action(string action)
    {
        var start = new ProcessStartInfo(_remote) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in new[] { "--host", Environment.GetEnvironmentVariable("EPLREMOTE_HOST") ?? throw new InvalidOperationException("Set EPLREMOTE_HOST to an IPv4 of this computer: the remote client does not connect by computer name."), action })
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["EPLREMOTE_PORT"] = Environment.GetEnvironmentVariable("EPLREMOTE_PORT") ?? "49152";
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd().Trim();
        string errors = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit();
        return output + (errors.Length > 0 ? " ERR " + errors[..Math.Min(errors.Length, 200)] : string.Empty);
    }

    /// <summary>Another process holds the macro file open for reading, sharing reading only: can EPLAN still insert it, and is a writer refused?</summary>
    private async Task MacroReadHandleAsync()
    {
        string page = (await _host.NewPagesAsync(1))[0];
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardInput = true, CreateNoWindow = true };
        foreach (string argument in new[] { "-NoProfile", "-Command", $"$f = [System.IO.File]::Open('{MacroFile.Replace("'", "''", StringComparison.Ordinal)}', 'Open', 'Read', 'Read'); 'held'; [Console]::In.ReadLine() | Out-Null; $f.Dispose(); 'released'" })
        {
            start.ArgumentList.Add(argument);
        }

        using Process holder = Process.Start(start)!;
        Log("holder: " + holder.StandardOutput.ReadLine());
        try
        {
            using var writer = new FileStream(MacroFile, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            Log("writer while held: OPENED (nothing written)");
        }
        catch (IOException ex)
        {
            Log("writer while held: refused, " + ex.GetType().Name + " 0x" + ex.HResult.ToString("X8", CultureInfo.InvariantCulture));
        }

        var clock = Stopwatch.StartNew();
        JsonElement inserted = await _host.InsertMacroAsync(page);
        Log($"macro.insert while held: {inserted.GetProperty("execution").GetString()}, {inserted.GetProperty("objectCount").GetInt32()} objects, {clock.ElapsedMilliseconds} ms");
        holder.StandardInput.WriteLine();
        Log("holder: " + holder.StandardOutput.ReadLine());
        holder.WaitForExit();
        await _host.DeleteAsync([page]);
    }

    private async Task CleanUpAsync(int first)
    {
        JsonElement list = await _host.CallAsync("eplan_page_list", new() { ["project"] = _host.Writable, ["name_prefix"] = DestructiveHost.Prefix, ["limit"] = 200 });
        string[] left = [.. list.GetProperty("pages").EnumerateArray().Select(p => p.GetProperty("name").GetString()!).Where(p => DestructiveHost.Number(p) >= Math.Min(first, 3))];
        if (left.Length > 0)
        {
            await _host.DeleteAsync(left);
        }

        Log("clean-up deleted: " + string.Join(", ", left));
    }
}
