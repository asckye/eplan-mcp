using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using EplanMcp.Host.Safety;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.Tests;

/// <summary>Redraws the detached sample exports through MCP and keeps discrepancies for the next gap list.</summary>
[Trait("Size", "Large")]
[Trait("Spike", "SmallProjectRedraw")]
public sealed class SmallProjectRedrawTests(LiveEplan eplan)
{
    private static readonly string[] _pages =
    [
        "&AAA1/1", "&AAB1/1",
        "=GAA+A1&EFS1/1", "=GAA+A1&EFS1/2",
        "=K1+A2&EFS1/1", "=K1+A2&EFS1/2",
        "=K1+A2&EFA1/1", "=K1+A2&EFA1/2", "=K1+A2&EFA1/3", "=K1+A2&EFA1/4", "=K1+A2&EFA1/5",
        "=K1+A2&EFA1/6", "=K1+A2&EFA1/7", "+A1&EMA2/2", "+A1&EPA1/1", "+A1&EMB1/1",
    ];

    [Fact]
    public async Task SixteenPages_McpRedraw_ReportsEveryDifferenceAndKeepsArtifacts()
    {
        string run = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8];
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp", "scratch", "inputs", "small-project");
        string targetFolder = Path.Combine(root, "target-" + run);
        Directory.CreateDirectory(targetFolder);
        string projectName = "SmallRedraw_" + run;
        string project = Path.Combine(LiveEplan.ScratchRoot(), "projects", projectName, projectName);
        var report = new SmallProjectRedrawReport(run, project);
        Dictionary<string, (JObject Page, string File)> source = LoadPages(Path.Combine(root, "source"), report);
        Dictionary<string, (JObject Page, string File)> target = new(StringComparer.Ordinal);
        Exception? infrastructureError = null;
        try
        {
            if (source.Count == 0)
            {
                report.Warning("", "No readable source pages. Export the sample with SmallProjectExport before this trial.");
                return;
            }

            string? basic;
            try
            { basic = ProjectCreateTests.Find("IEC_bas003_sample_project.zw9"); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                report.Warning("", "Basic project lookup is unreadable: " + error.Message);
                return;
            }
            if (basic is null)
            {
                report.Warning("", "No unique IEC_bas003_sample_project.zw9 below the public EPLAN data directory; project creation was not attempted.");
                return;
            }
            string relative = "small-project/" + run + "/" + Path.GetFileName(basic);
            string imports = Path.Combine(LiveEplan.DataRoot(), "imports");
            string imported = Path.Combine(imports, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(imported)!);
            try
            { File.Copy(basic, imported); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                report.Warning("", "Basic project could not be copied to imports: " + error.Message);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetDirectoryName(project))!);
            JObject? created = await ObserveAsync(report, "", "eplan_project_create", "Project", new JObject
            {
                ["mode"] = "basic_project",
                ["file"] = relative,
                ["name"] = projectName,
                ["destination"] = Path.GetDirectoryName(project),
                ["dry_run"] = false,
            });
            string? link = (string?)created?["project"]?["path"];
            if (link is null || !string.Equals(Path.GetFullPath(link), Path.GetFullPath(project + ".elk"), StringComparison.OrdinalIgnoreCase)
                || !ScratchPaths.IsInside(link, [LiveEplan.ScratchRoot()]))
            {
                throw new InvalidOperationException("The create tool did not confirm the expected new scratch project; no drawing calls were sent.");
            }

            // ProjectOpen is the explicitly allowed setup exception; every drawing/read call below is MCP.
            await PlanStepLiveSession.ActionAsync("ProjectOpen /Project:\"" + link + "\" /Quiet");
            var driver = new SmallProjectRedrawDriver(CallAsync, project, imports, report);
            JObject[] ordered = [.. source.Values.Select(value => value.Page).OrderBy(IsReportPage)
                .ThenBy(page => (string?)page["pageType"] == "Overview" ? 0 : 1)];
            foreach (JObject page in ordered)
            {
                string name = (string)page["name"]!;
                Print("Redrawing " + name);
                try
                { await driver.DrawPageAsync(page, ordered); }
                catch (Exception error)
                {
                    infrastructureError ??= error;
                    report.InfrastructureError(name, error.ToString());
                }
            }

            await SelectCurrentAsync(report, project);
            await ExportNativeAsync(project, targetFolder, report);
            target = LoadPages(targetFolder, report);

            string fixture = Path.Combine(LiveEplan.ScratchRoot(), "fixtures", LiveEplan.ProjectName);
            // Source image reads never select, edit, or open the source fixture through native helpers.
            foreach (string name in _pages)
            {
                await ExportImageAsync(report, fixture, name, Path.Combine(targetFolder, "images", "source"));
                await ExportImageAsync(report, project, name, Path.Combine(targetFolder, "images", "target"));
            }
        }
        catch (Exception error)
        {
            infrastructureError = error;
            report.InfrastructureError("redraw/export infrastructure", error.ToString());
        }
        finally
        {
            foreach (string name in _pages)
            {
                source.TryGetValue(name, out var original);
                target.TryGetValue(name, out var redrawn);
                report.AddPage(name, original.Page, redrawn.Page, original.File, redrawn.File);
            }
            string json = Path.Combine(root, "report-" + run + ".json");
            string markdown = Path.Combine(root, "report-" + run + ".md");
            await File.WriteAllTextAsync(json, report.ToJson().ToString(), CancellationToken.None);
            string summary = report.ToMarkdown();
            await File.WriteAllTextAsync(markdown, summary, CancellationToken.None);
            Print(summary);
            Print("Report: " + json + Environment.NewLine + markdown);
        }
        Assert.True(infrastructureError is null, infrastructureError?.ToString());
    }

    private static bool IsReportPage(JObject page) => (bool?)page["isReportPage"] == true
        || page["report"] is JContainer { Count: > 0 } || (string?)page["pageType"] is not ("Circuit" or "Overview");

    private static Dictionary<string, (JObject Page, string File)> LoadPages(string folder, SmallProjectRedrawReport report)
    {
        var pages = new Dictionary<string, (JObject, string)>(StringComparer.Ordinal);
        if (!Directory.Exists(folder))
        {
            report.Warning("", "Export folder is missing: " + folder);
            return pages;
        }
        string[] files;
        try
        { files = Directory.GetFiles(folder, "*.json").Order(StringComparer.Ordinal).ToArray(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            report.Warning("", "Export folder is unreadable: " + folder + ": " + error.Message);
            return pages;
        }
        var duplicateNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in files)
        {
            try
            {
                JObject page = JObject.Parse(File.ReadAllText(file));
                string? name = (string?)page["name"];
                if (name is null || !_pages.Contains(name, StringComparer.Ordinal))
                { continue; }
                if (pages.ContainsKey(name) || duplicateNames.Contains(name))
                {
                    pages.Remove(name);
                    duplicateNames.Add(name);
                    report.Warning(name, "Duplicate exported page name; no input chosen: " + file);
                    continue;
                }
                pages.Add(name, (page, file));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
            { report.Warning("", "Unreadable export " + file + ": " + error.Message); }
        }
        return pages;
    }

    private async Task<JObject> CallAsync(string tool, JObject arguments)
    {
        using JsonDocument document = JsonDocument.Parse(arguments.ToString());
        Dictionary<string, object?> args = document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => (object?)property.Value.Clone(), StringComparer.Ordinal);
        (bool succeeded, JsonElement result) = await eplan.CallOrErrorAsync(tool, args);
        JObject body = JObject.Parse(result.GetRawText());
        return succeeded ? body : new JObject { ["error"] = body };
    }

    private async Task<JObject?> ObserveAsync(SmallProjectRedrawReport report, string page, string tool, string type, JObject arguments)
    {
        JObject? result = null;
        try
        {
            result = await CallAsync(tool, arguments);
            bool succeeded = result["error"] is null;
            report.RecordCall(page, tool, [type], [], arguments, result, succeeded, result["error"]?.ToString());
            return succeeded ? result : null;
        }
        catch (Exception error)
        {
            report.RecordCall(page, tool, [type], [], arguments, result, false, error.ToString());
            return null;
        }
    }

    private async Task SelectCurrentAsync(SmallProjectRedrawReport report, string project)
    {
        JObject? projects = await ObserveAsync(report, "", "eplan_project_list", "Project", new JObject { ["limit"] = 200 });
        if (projects?["projects"] is JArray open && open.OfType<JObject>().Any(item =>
            (bool?)item["current"] == true && string.Equals((string?)item["path"], project, StringComparison.OrdinalIgnoreCase)))
        { return; }
        JObject? pages = await ObserveAsync(report, "", "eplan_page_list", "Page", new JObject { ["project"] = project, ["limit"] = 1 });
        string? page = (string?)pages?["pages"]?.First?["name"];
        if (page is null || await ObserveAsync(report, page, "eplan_page_open", "Page", new JObject { ["project"] = project, ["page"] = page }) is null)
        { throw new InvalidOperationException("Cannot select the new scratch project for native export."); }
    }

    private async Task ExportImageAsync(SmallProjectRedrawReport report, string project, string page, string folder)
    {
        JObject? image = await ObserveAsync(report, page, "eplan_export_image", "Page", new JObject
        { ["project"] = project, ["pages"] = new JArray(page), ["format"] = "png", ["width_px"] = 2400 });
        if (image is null)
        { return; }
        if (image["files"] is not JArray files || (string?)image["directory"] is not string exported)
        {
            report.Warning(page, "Image export returned no readable file manifest: " + image);
            return;
        }
        foreach (JObject file in files.OfType<JObject>())
        {
            try
            {
                string path = Path.GetFullPath(Path.Combine(exported, (string?)file["path"] ?? ""));
                if (!ScratchPaths.IsInside(path, [exported]))
                { throw new IOException("Image manifest path is outside its export folder."); }
                string pageFolder = Path.Combine(folder, (Array.IndexOf(_pages, page) + 1).ToString("D2", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(pageFolder);
                string destination = Path.Combine(pageFolder, Path.GetFileName(path));
                File.Copy(path, destination, overwrite: false);
                report.Note(page, "Image", "", "Copied exported image: " + destination);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            { report.Warning(page, "Image copy failed: " + error.Message); }
        }
    }

    private static async Task ExportNativeAsync(string project, string folder, SmallProjectRedrawReport report)
    {
        string helper = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.ConnectionHelper", "EplanMcp.Large.ConnectionHelper.dll");
        if (!File.Exists(helper))
        { throw new FileNotFoundException("Build EplanMcp.Large.ConnectionHelper before running the redraw.", helper); }
        string request = Path.Combine(folder, "export-request.json");
        await File.WriteAllTextAsync(request, new JObject
        { ["project"] = project, ["pages"] = new JArray(_pages), ["output"] = folder }.ToString(), TestContext.Current.CancellationToken);
        await PlanStepLiveSession.ActionAsync("EplApiModuleAction /register:\"" + helper + "\" /Quiet");
        try
        {
            await ExportActionAsync(request);
            JObject result = JObject.Parse(await File.ReadAllTextAsync(request + ".result.json", TestContext.Current.CancellationToken));
            if ((bool?)result["success"] != true)
            { throw new InvalidOperationException("Native page export failed: " + result); }
            report.Note("", "Page", "", "Native export manifest: " + request + ".result.json");
        }
        finally
        { await PlanStepLiveSession.ActionAsync("EplApiModuleAction /unregister:\"EplanMcp.Large.ConnectionHelper\" /Quiet"); }
    }

    private static async Task ExportActionAsync(string request)
    {
        string remote = Environment.GetEnvironmentVariable("EPLANMCP_EPLREMOTE")
            ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".local", "tools", "eplremote", "bin", "Release", "net481", "eplremote.exe"));
        var start = new ProcessStartInfo(remote)
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("--host");
        start.ArgumentList.Add(Environment.GetEnvironmentVariable("EPLREMOTE_HOST")
            ?? throw new InvalidOperationException("Set EPLREMOTE_HOST to this machine's IPv4 address (S35/A41)."));
        start.ArgumentList.Add("EplanMcpLargePageExport /REQUEST:\"" + request + "\"");
        start.Environment["EPLREMOTE_PORT"] = Environment.GetEnvironmentVariable("EPLREMOTE_PORT") ?? "49152";
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start eplremote.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));
        await process.WaitForExitAsync(timeout.Token);
        if (process.ExitCode != 0)
        { throw new InvalidOperationException($"Page export action failed ({process.ExitCode}): {await stdout} {await stderr}"); }
    }

    private static void Print(string message) => TestContext.Current.TestOutputHelper!.WriteLine(message);
}
