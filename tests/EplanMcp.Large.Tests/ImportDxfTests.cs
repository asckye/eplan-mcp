using System.Globalization;
using System.Text;
using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_import_dxf on the write copy (README.md), through the write tests' eplanmcp, whose data root is a temporary
/// folder: its <c>imports</c> folder gets the files, which the tests make themselves (docs/tools/eplan_import_dxf.md,
/// section 7; SEC-31). One is a small DXF written here; the other is EPLAN's own Rittal drawing that S22 imported
/// (api-notes A29), copied from EPLAN's data folder when it is there. Each run imports onto new pages after the last one
/// of =GAA+A1&amp;EFS1; the copy keeps them. Images of the pages, exported through the read tests' eplanmcp, go to the
/// test output, to look at where the drawings lie.
/// </summary>
[Trait("Size", "Large")]
public sealed class ImportDxfTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    private const string Prefix = "=GAA+A1&EFS1/";

    /// <summary>The Rittal drawing S22 imported, below EPLAN's data folder (<c>%PUBLIC%\EPLAN\Data</c>).</summary>
    private static readonly string? _rittal = Environment.GetEnvironmentVariable("PUBLIC") is { Length: > 0 } shared
        ? Path.Combine(shared, "EPLAN", "Data", "DXF_DWG", "Company name", "RITTAL", "4685200F.DXF")
        : null;

    [Fact]
    public async Task ASmallDrawing_IsImportedOnce_AndAgainAsASecondCopy()
    {
        string file = WriteImport("large/rectangle.dxf", Encoding.ASCII.GetBytes(SmallDxf()));
        string page = await NewPageAsync();
        Dictionary<string, object?> args = new() { ["project"] = host.Writable, ["page"] = page, ["file"] = file };

        JsonElement dryRun = await host.CallAsync("eplan_import_dxf", new(args) { ["dry_run"] = true });
        JsonElement imported = await host.CallAsync("eplan_import_dxf", args);
        JsonElement again = await host.CallAsync("eplan_import_dxf", args);

        Assert.Equal(("not_started", file, JsonValueKind.Null), (dryRun.GetProperty("execution").GetString(), dryRun.GetProperty("file").GetString(), dryRun.GetProperty("objectsAdded").ValueKind));
        Assert.Equal(64, dryRun.GetProperty("sha256").GetString()!.Length);
        Assert.Equal(("committed", "AI: eplan_import_dxf"), (imported.GetProperty("execution").GetString(), imported.GetProperty("undo").GetString()));
        int objects = imported.GetProperty("objectsAdded").GetInt32();
        Assert.InRange(objects, 1, 100);

        // Not idempotent (tool spec 6): the same drawing again adds as many objects again.
        Assert.Equal(objects, again.GetProperty("objectsAdded").GetInt32());

        // The approval and the audit name the file and its SHA-256 (M4c design 4.5).
        Assert.Contains(host.AuditRecords(), r => (string?)r["tool"] == "eplan_import_dxf" && r["arguments"]?["sha256"]?.GetValue<string>() == dryRun.GetProperty("sha256").GetString());
        await WriteImageAsync(page, $"small DXF, 4 lines and a circle: {objects} objects, fit and center");
    }

    [Fact]
    public async Task TheRittalDrawing_AddsS22s227Objects_ScaledOrInItsOwnSize()
    {
        Assert.SkipWhen(_rittal is null || !File.Exists(_rittal), $"EPLAN's sample drawing is not at {_rittal ?? "%PUBLIC%"}.");
        string file = WriteImport("rittal/4685200F.DXF", File.ReadAllBytes(_rittal!));
        string fit = await NewPageAsync();

        JsonElement centred = await host.CallAsync("eplan_import_dxf", new() { ["project"] = host.Writable, ["page"] = fit, ["file"] = file });
        string own = await NewPageAsync();
        JsonElement lowerLeft = await host.CallAsync("eplan_import_dxf", new() { ["project"] = host.Writable, ["page"] = own, ["file"] = file, ["scale"] = "none", ["position"] = "lower_left" });

        // S22 (A29): ScalingMode.Automatic, PositionMode.Center gave the page 227 objects.
        Assert.Equal(227, centred.GetProperty("objectsAdded").GetInt32());
        Assert.Equal("committed", lowerLeft.GetProperty("execution").GetString());
        await WriteImageAsync(fit, "Rittal 4685200F.DXF, fit and center (S22)");
        await WriteImageAsync(own, $"Rittal 4685200F.DXF, none and lower_left: {lowerLeft.GetProperty("objectsAdded").GetInt32()} objects");
    }

    [Fact]
    public async Task AMissingFile_APathOutTheFolder_AndAnotherExtension_ChangeNothing()
    {
        WriteImport("large/notes.txt", [1]);
        string page = Prefix + "1";

        JsonElement missing = await host.ErrorAsync("eplan_import_dxf", new() { ["project"] = host.Writable, ["page"] = page, ["file"] = "large/missing.dxf" });
        JsonElement outside = await host.ErrorAsync("eplan_import_dxf", new() { ["project"] = host.Writable, ["page"] = page, ["file"] = "../config.dxf" });
        JsonElement other = await host.ErrorAsync("eplan_import_dxf", new() { ["project"] = host.Writable, ["page"] = page, ["file"] = "large/notes.txt" });

        Assert.Equal((ErrorReason.FileNotFound, ExecutionState.NotStarted), (missing.GetProperty("reason").GetString(), missing.GetProperty("execution").GetString()));
        Assert.Equal(ErrorReason.InvalidArgument, outside.GetProperty("reason").GetString());
        Assert.Equal(ErrorReason.InvalidArgument, other.GetProperty("reason").GetString());
        Assert.DoesNotContain(host.Home, missing.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheCopyOutsideScratch_IsPolicyDenied()
    {
        string file = WriteImport("large/rectangle.dxf", Encoding.ASCII.GetBytes(SmallDxf()));

        JsonElement error = await host.ErrorAsync("eplan_import_dxf", new() { ["project"] = host.Outside, ["page"] = Prefix + "1", ["file"] = file });

        Assert.Equal((ErrorReason.PolicyDenied, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
    }

    /// <summary>
    /// A DXF of AutoCAD R12 (AC1009), the oldest version CAD programs still read: a 200 x 100 rectangle of four lines and
    /// a circle of radius 30 in its middle, on layer 0, with the tables that layer needs.
    /// </summary>
    private static string SmallDxf()
    {
        var dxf = new StringBuilder();
        void Pair(int code, string value) => dxf.Append(code.ToString(CultureInfo.InvariantCulture)).Append("\r\n").Append(value).Append("\r\n");
        void Line(double x1, double y1, double x2, double y2)
        {
            Pair(0, "LINE");
            Pair(8, "0");
            Pair(10, Number(x1));
            Pair(20, Number(y1));
            Pair(30, "0.0");
            Pair(11, Number(x2));
            Pair(21, Number(y2));
            Pair(31, "0.0");
        }

        Pair(0, "SECTION");
        Pair(2, "HEADER");
        Pair(9, "$ACADVER");
        Pair(1, "AC1009");
        Pair(9, "$EXTMIN");
        Pair(10, "0.0");
        Pair(20, "0.0");
        Pair(30, "0.0");
        Pair(9, "$EXTMAX");
        Pair(10, "200.0");
        Pair(20, "100.0");
        Pair(30, "0.0");
        Pair(0, "ENDSEC");
        Pair(0, "SECTION");
        Pair(2, "TABLES");
        Pair(0, "TABLE");
        Pair(2, "LTYPE");
        Pair(70, "1");
        Pair(0, "LTYPE");
        Pair(2, "CONTINUOUS");
        Pair(70, "0");
        Pair(3, "Solid line");
        Pair(72, "65");
        Pair(73, "0");
        Pair(40, "0.0");
        Pair(0, "ENDTAB");
        Pair(0, "TABLE");
        Pair(2, "LAYER");
        Pair(70, "1");
        Pair(0, "LAYER");
        Pair(2, "0");
        Pair(70, "0");
        Pair(62, "7");
        Pair(6, "CONTINUOUS");
        Pair(0, "ENDTAB");
        Pair(0, "ENDSEC");
        Pair(0, "SECTION");
        Pair(2, "ENTITIES");
        Line(0, 0, 200, 0);
        Line(200, 0, 200, 100);
        Line(200, 100, 0, 100);
        Line(0, 100, 0, 0);
        Pair(0, "CIRCLE");
        Pair(8, "0");
        Pair(10, "100.0");
        Pair(20, "50.0");
        Pair(30, "0.0");
        Pair(40, "30.0");
        Pair(0, "ENDSEC");
        Pair(0, "EOF");
        return dxf.ToString();
    }

    private static string Number(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>Writes a file below this eplanmcp's import folder and returns its path as the tool takes it.</summary>
    private string WriteImport(string relative, byte[] content)
    {
        string path = Path.Combine(host.Home, "imports", relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return relative;
    }

    private async Task WriteImageAsync(string page, string what)
    {
        // Through the read tests' eplanmcp, so the image stays in the engineer's export folder after the run.
        JsonElement image = await eplan.CallAsync("eplan_export_image", new() { ["project"] = host.Writable, ["pages"] = JsonSerializer.SerializeToElement(new[] { page }) });
        TestContext.Current.TestOutputHelper?.WriteLine($"{page}: {what}. Look at the image in {image.GetProperty("directory").GetString()}.");
    }

    /// <summary>A new circuit page after the last numbered page of =GAA+A1&amp;EFS1 in the copy.</summary>
    private async Task<string> NewPageAsync()
    {
        int last = 0;
        string? cursor = null;
        do
        {
            Dictionary<string, object?> args = new() { ["project"] = host.Writable, ["name_prefix"] = Prefix, ["limit"] = 200 };
            if (cursor is not null)
            {
                args["cursor"] = cursor;
            }

            JsonElement list = await host.CallAsync("eplan_page_list", args);
            foreach (JsonElement page in list.GetProperty("pages").EnumerateArray())
            {
                if (int.TryParse(page.GetProperty("name").GetString()![Prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                {
                    last = Math.Max(last, number);
                }
            }

            cursor = list.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        string name = Prefix + (last + 1).ToString(CultureInfo.InvariantCulture);
        await host.CallAsync("eplan_page_create", new() { ["project"] = host.Writable, ["pages"] = JsonSerializer.SerializeToElement(new[] { new { name } }) });
        return name;
    }
}
