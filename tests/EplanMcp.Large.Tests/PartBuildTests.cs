using System.Text.Json;
using System.Xml.Linq;
using EplanMcp.Contracts;
using EplanMcp.Host;
using EplanMcp.Host.Safety;

namespace EplanMcp.Large.Tests;

/// <summary>Never changes database settings. Requires an engineer-selected, verified copy, in addition to the import opt-in.</summary>
[Trait("Size", "Large")]
public sealed class PartBuildTests
{
    [Fact]
    public async Task ThreeFictionalParts_AreBuiltOnlyOnACopy_ThenExportedAndCompared()
    {
        PartImportTests.SkipUnlessOptedIn();
        string? configuredCopy = Environment.GetEnvironmentVariable("EPLANMCP_LARGE_PARTS_COPY");
        string? configuredSource = Environment.GetEnvironmentVariable("EPLANMCP_LARGE_PARTS_SOURCE");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(configuredCopy) && !string.IsNullOrWhiteSpace(configuredSource), "Set PARTS_COPY and PARTS_SOURCE to the engineer-selected absolute .alk paths; configure EPLAN with the copy's absolute path before starting.");
        Assert.True(Path.IsPathFullyQualified(configuredCopy!) && Path.IsPathFullyQualified(configuredSource!));
        string copy = ScratchPaths.Canonical(configuredCopy!);
        string source = ScratchPaths.Canonical(configuredSource!);
        Assert.EndsWith(".alk", copy, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".alk", source, StringComparison.OrdinalIgnoreCase);
        Assert.True(ScratchPaths.IsInside(copy, [LiveEplan.ScratchRoot()]));
        Assert.False(string.Equals(copy, source, StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(copy) && File.Exists(source));
        string copyFolder = Path.ChangeExtension(copy, ".adb");
        string sourceFolder = Path.ChangeExtension(source, ".adb");
        Assert.True(Directory.Exists(copyFolder) && Directory.Exists(sourceFolder));
        Assert.All(Directory.EnumerateFileSystemEntries(copyFolder, "*", SearchOption.AllDirectories).Append(copy).Append(copyFolder), p => Assert.Equal(0, (int)(File.GetAttributes(p) & FileAttributes.ReparsePoint)));
        Dictionary<string, string> original = PartExportTests.Hashes(sourceFolder);
        // The data files must be a fresh byte-identical copy; session and lock files are excluded by the established helper.
        Dictionary<string, string> clone = PartExportTests.Hashes(copyFolder);
        Assert.Equal(original.Where(p => p.Key.StartsWith(sourceFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).Select(p => (Path.GetRelativePath(sourceFolder, p.Key), p.Value)).OrderBy(p => p.Item1, StringComparer.Ordinal), clone.Where(p => p.Key.StartsWith(copyFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).Select(p => (Path.GetRelativePath(copyFolder, p.Key), p.Value)).OrderBy(p => p.Item1, StringComparer.Ordinal));
        await using var host = new DestructiveHost { RequireWritable = false };
        host.Approvals.TargetCheck = question => question.Plan.MasterData is { } target
            && string.Equals(target.Database, copy, StringComparison.OrdinalIgnoreCase)
            && string.Equals(ScratchPaths.Canonical(target.Path), copy, StringComparison.OrdinalIgnoreCase);
        await host.InitializeAsync();
        try
        {
            const string csv = "part_number,manufacturer,type_number,description.en_US,width,technical_data,function_category,connection_points\nFICTION.BUILD1,FICTION,R1,Fictional relay,12.5,DC 24V,200,A1|A2\nFICTION.BUILD2,FICTION,C1,Fictional contact,10,AC 230V,,\nFICTION.BUILD3,FICTION,T1,Fictional terminal,5,16 A,,";
            string imports = new HostDataLocations(host.Home).Imports;
            Directory.CreateDirectory(imports);
            string table = Path.Combine(imports, "fictional-parts.csv");
            File.WriteAllText(table, csv);
            JsonElement preview = await host.CallAsync("eplan_part_build", new() { ["file"] = "fictional-parts.csv", ["dry_run"] = true });
            // A variable or any other configured database fails closed before the first write.
            Assert.Equal(copy, Path.GetFullPath(preview.GetProperty("database").GetString()!), ignoreCase: true);
            Assert.All(preview.GetProperty("rows").EnumerateArray(), row => Assert.Equal("create", row.GetProperty("action").GetString()));
            Assert.Empty(host.Approvals.Questions);
            var changes = new List<PlannedChange> { new() { Action = "read", Kind = "source", Name = "fictional-parts.csv", Property = "bytes / sha256", NewValue = FormattableString.Invariant($"{new FileInfo(table).Length} / {preview.GetProperty("sha256").GetString()}") } };
            foreach (JsonElement row in preview.GetProperty("rows").EnumerateArray())
            {
                foreach (JsonProperty field in row.GetProperty("diff").EnumerateObject())
                { changes.Add(new() { Action = "create", Kind = "part", Name = row.GetProperty("partNumber").GetString()!, Property = field.Name, OldValue = field.Value[0].GetString(), NewValue = field.Value[1].GetString() }); }
            }
            host.Approvals.Approve("eplan_part_build", string.Empty, WritePipeline.ChangeLines(new WritePlan { Changes = changes }));
            JsonElement built = await host.CallAsync("eplan_part_build", new() { ["file"] = "fictional-parts.csv", ["dry_run"] = false });
            Assert.Equal("committed", built.GetProperty("execution").GetString());
            Assert.Equal(3, built.GetProperty("import").GetProperty("addedCount").GetInt32());
            Assert.Empty(built.GetProperty("import").GetProperty("missing").EnumerateArray());
            Assert.True(File.Exists(built.GetProperty("import").GetProperty("backup").GetString()));
            string[] numbers = ["FICTION.BUILD1", "FICTION.BUILD2", "FICTION.BUILD3"];
            JsonElement exported = await host.CallAsync("eplan_part_export", new() { ["parts"] = numbers });
            string xml = Path.Combine(exported.GetProperty("directory").GetString()!, exported.GetProperty("files")[0].GetProperty("path").GetString()!);
            XElement[] parts = XDocument.Load(xml).Root!.Elements("part").OrderBy(p => (string?)p.Attribute("P_ARTICLE_PARTNR"), StringComparer.Ordinal).ToArray();
            Assert.Equal(numbers, parts.Select(p => (string?)p.Attribute("P_ARTICLE_PARTNR")));
            string[] types = ["R1", "C1", "T1"], descriptions = ["Fictional relay", "Fictional contact", "Fictional terminal"], widths = ["12.5", "10", "5"], technical = ["DC 24V", "AC 230V", "16 A"];
            for (int i = 0; i < parts.Length; i++)
            {
                Assert.Equal("FICTION", (string?)parts[i].Attribute("P_ARTICLE_MANUFACTURER"));
                Assert.Equal(types[i], (string?)parts[i].Attribute("P_ARTICLE_TYPENR"));
                Assert.Contains("en_US@" + descriptions[i] + ";", (string?)parts[i].Attribute("P_ARTICLE_DESCR1"), StringComparison.Ordinal);
                Assert.Equal(widths[i], (string?)parts[i].Attribute("P_ARTICLE_WIDTH"));
                XElement variant = Assert.Single(parts[i].Elements("variant"));
                Assert.Equal("1", (string?)variant.Attribute("P_ARTICLE_VARIANT"));
                Assert.Equal(technical[i], (string?)variant.Attribute("P_ARTICLE_CHARACTERISTICS"));
                if (i == 0)
                {
                    XElement template = Assert.Single(variant.Elements("functiontemplate"));
                    Assert.Equal("200", (string?)template.Attribute("functiondefcategory"));
                    Assert.Equal("A1\nA2", (string?)template.Attribute("connectionDesignation"));
                }
            }
        }
        finally { PartExportTests.AssertUnchanged(original, PartExportTests.Hashes(sourceFolder)); }
    }
}
