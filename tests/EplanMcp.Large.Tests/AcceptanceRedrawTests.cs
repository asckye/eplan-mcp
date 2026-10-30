using System.Globalization;
using System.Text.Json;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class AcceptanceRedrawTests(LiveEplan eplan)
{
    private static readonly JsonSerializerOptions _reportOptions = new() { WriteIndented = true };

    [Fact]
    public async Task DrawingList_NewLargeWritePages_ReadbackAndImage_LeaveForViewing()
    {
        string repository = Repository();
        JsonElement list = await AcceptanceListFile.LoadAsync(repository,
            Path.Combine(AppContext.BaseDirectory, "fixtures", "acceptance", "fictional-plc-di-page.json"),
            Environment.GetEnvironmentVariable(AcceptanceListFile.Variable), TestContext.Current.CancellationToken);
        JsonElement projects = await eplan.CallAsync("eplan_project_list", new() { ["limit"] = 200 });
        Assert.False(projects.GetProperty("truncated").GetBoolean());
        JsonElement copy = Assert.Single(projects.GetProperty("projects").EnumerateArray(), p =>
            string.Equals(Path.GetFileName(p.GetProperty("path").GetString()), WriteHost.WritableName, StringComparison.Ordinal));
        string project = copy.GetProperty("path").GetString()!;
        Assert.True(Host.Safety.ScratchPaths.IsInside(project, [WriteHost.ScratchFolder]));
        int number = await LastPageNumberAsync(project) + 1;
        string run = number.ToString(CultureInfo.InvariantCulture);
        string artifacts = Path.Combine(WriteHost.ScratchFolder, "acceptance-results", run + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(artifacts);
        void Output(string message) => TestContext.Current.TestOutputHelper!.WriteLine(message);
        var driver = new AcceptanceRedrawDriver(eplan.CallOrErrorAsync, project, list, run, Output, (number + 1).ToString(CultureInfo.InvariantCulture));
        string status = "failed";
        try
        {
            await driver.DrawAsync();
            await driver.ExportAsync();
            JsonElement image = driver.Image!.Value;
            string exportRoot = image.GetProperty("directory").GetString()!;
            foreach (JsonElement file in image.GetProperty("files").EnumerateArray())
            {
                string source = Path.GetFullPath(Path.Combine(exportRoot, file.GetProperty("path").GetString()!));
                Assert.True(Host.Safety.ScratchPaths.IsInside(source, [exportRoot]));
                Assert.True(new FileInfo(source).Length > 0);
                File.Copy(source, Path.Combine(artifacts, Path.GetFileName(source)));
            }
            status = driver.SkippedCount > 0 ? "skipped" : "passed";
            Assert.SkipWhen(driver.SkippedCount > 0, "Whole-page acceptance is incomplete; inspect skipped items and local evidence in " + artifacts);
        }
        finally
        {
            // Keep pages, editor and artifacts intact, including on failure. Never invoke undo or delete here.
            await File.WriteAllTextAsync(Path.Combine(artifacts, "acceptance-report.json"),
                JsonSerializer.Serialize(new { status, drawing = driver.Report }, _reportOptions), CancellationToken.None);
            Output($"Acceptance {status}: {driver.Page}; counterpart: {driver.CounterpartPage}; confirmed undo steps: {driver.UndoSteps}; count incomplete: {driver.UndoCountIncomplete}; evidence: {artifacts}");
        }
    }

    // Pages under a new structure segment are rejected (S063133), so the run takes fresh numbers under the sample's =GAA+A1&EFS1.
    private async Task<int> LastPageNumberAsync(string project)
    {
        const string Prefix = "=GAA+A1&EFS1/";
        int last = 0;
        string? cursor = null;
        do
        {
            Dictionary<string, object?> args = new() { ["project"] = project, ["name_prefix"] = Prefix, ["limit"] = 200 };
            if (cursor is not null)
            {
                args["cursor"] = cursor;
            }

            JsonElement pages = await eplan.CallAsync("eplan_page_list", args);
            foreach (JsonElement page in pages.GetProperty("pages").EnumerateArray())
            {
                if (int.TryParse(page.GetProperty("name").GetString()![Prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int value))
                {
                    last = Math.Max(last, value);
                }
            }

            cursor = pages.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);
        return last;
    }

    private static string Repository()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        { if (File.Exists(Path.Combine(directory.FullName, "EplanMcp.slnx"))) { return directory.FullName; } }
        throw new InvalidOperationException("EplanMcp.slnx was not found above the test output.");
    }
}
