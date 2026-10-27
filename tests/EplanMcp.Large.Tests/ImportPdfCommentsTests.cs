using System.Security.Cryptography;
using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>eplan_import_pdf_comments on a fresh LargeWrite copy, using only a locally supplied Reader PDF (S44).</summary>
[Trait("Size", "Large")]
public sealed class ImportPdfCommentsTests(WriteHost host) : IClassFixture<WriteHost>
{
    public static bool PdfAvailable => Environment.GetEnvironmentVariable("EPLANMCP_LARGE_PDF_COMMENTS") is { Length: > 0 } path && File.Exists(path);

    [Fact(SkipUnless = nameof(PdfAvailable), Skip = "Set EPLANMCP_LARGE_PDF_COMMENTS to a local Reader-annotated PDF under the scratch inputs folder (never committed).")]
    public async Task StickyNotes_MapToPageOne_AndReimportAddsNothing()
    {
        string source = Path.GetFullPath(Environment.GetEnvironmentVariable("EPLANMCP_LARGE_PDF_COMMENTS")!);
        string inputs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp", "scratch", "inputs");
        Assert.StartsWith(inputs + Path.DirectorySeparatorChar, source, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".pdf", source, StringComparison.OrdinalIgnoreCase);
        Assert.False(source.StartsWith(@"\\", StringComparison.Ordinal));
        string current = inputs;
        Assert.Equal((FileAttributes)0, File.GetAttributes(current) & FileAttributes.ReparsePoint);
        foreach (string component in Path.GetRelativePath(inputs, source).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, component);
            Assert.Equal((FileAttributes)0, File.GetAttributes(current) & FileAttributes.ReparsePoint);
        }

        byte[] originalHash = Hash(source);
        string file = "pdf-comments-" + Guid.NewGuid().ToString("N") + ".pdf";
        string imports = Directory.CreateDirectory(Path.Combine(host.Home, "imports")).FullName;
        File.Copy(source, Path.Combine(imports, file));
        Dictionary<string, object?> args = new() { ["project"] = host.Writable, ["file"] = file };
        JsonElement dryRun = await host.CallAsync("eplan_import_pdf_comments", new(args) { ["dry_run"] = true });
        Assert.Equal("not_started", dryRun.GetProperty("execution").GetString());
        Assert.Equal(JsonValueKind.Null, dryRun.GetProperty("added").ValueKind);
        Assert.Equal(0, dryRun.GetProperty("before").GetInt32()); // Reset the closed scratch fixture before rerunning.

        JsonElement imported = await host.CallAsync("eplan_import_pdf_comments", args);
        int added = imported.GetProperty("added").GetInt32();
        Assert.True(added >= 1);
        Assert.Equal(("committed", "AI: eplan_import_pdf_comments"), (imported.GetProperty("execution").GetString(), imported.GetProperty("undo").GetString()));
        Assert.Contains(imported.GetProperty("comments").EnumerateArray(), c =>
            c.GetProperty("page").GetString() == "=GAA+A1&EFS1/1" && c.GetProperty("language").GetString() == "??_??" &&
            !string.IsNullOrEmpty(c.GetProperty("text").GetString()));
        Assert.Equal(imported.GetProperty("before").GetInt32() + added, imported.GetProperty("after").GetInt32());

        JsonElement again = await host.CallAsync("eplan_import_pdf_comments", args);
        Assert.Equal(("not_started", 0, JsonValueKind.Null), (again.GetProperty("execution").GetString(), again.GetProperty("added").GetInt32(), again.GetProperty("undo").ValueKind));
        Assert.Equal(imported.GetProperty("after").GetInt32(), again.GetProperty("after").GetInt32());
        Assert.Empty(again.GetProperty("comments").EnumerateArray());
        Assert.Equal(originalHash, Hash(source));
        Assert.Equal(originalHash, Hash(Path.Combine(imports, file)));
        TestContext.Current.TestOutputHelper?.WriteLine("Manual only: with LargeWrite current, one XEsUndoAction-style undo must remove the imported notes; a second undo must reach the preceding sentinel. Do not automate undo (A41/S44).");
    }

    private static byte[] Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }
}
