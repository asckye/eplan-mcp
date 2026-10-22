using EplanMcp.Contracts;
using EplanMcp.Host.Importing;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Safety;
using EplanMcp.Packs;
using EplanMcp.Packs.Scanning;

namespace EplanMcp.Host.Tests.Packs;

/// <summary>The forms of eplan_pack_import's inputs (tool spec 2, SEC-31, SEC-36); reads nothing.</summary>
[Trait("Size", "Small")]
public sealed class PackInputsParseTests
{
    private static readonly HostConfig _config = HostConfig.Defaults(new HostDataLocations("C:\\Data\\EplanMcp")) with
    {
        PackSources = new Dictionary<string, string> { ["company-refs"] = "\\\\server\\refs" },
    };

    [Theory]
    [InlineData("refs/Plant", null, null, "refs/Plant")]
    [InlineData(" macros\\motor.ema ", null, null, "macros/motor.ema")]
    [InlineData("rule=company/rules.md", ResourceKind.Rule, null, "company/rules.md")]
    [InlineData("material_mapping=parts.csv", ResourceKind.MaterialMapping, null, "parts.csv")]
    [InlineData("company-refs:Projects/Plant.elk", null, "company-refs", "Projects/Plant.elk")]
    [InlineData("document=company-refs:guide.md", ResourceKind.Document, "company-refs", "guide.md")]
    public void Parse_GoodInputs_GiveKindSourceAndPath(string given, ResourceKind? kind, string? source, string path)
    {
        var violations = new List<string>();

        PackInputArgument? input = PackInputs.Parse("inputs[0]", given, _config, violations);

        Assert.Empty(violations);
        Assert.Equal(new PackInputArgument(kind, source, path), input);
    }

    [Theory]
    [InlineData("C:\\Refs\\Plant", "no drive")]
    [InlineData("\\\\server\\share\\Plant", "no drive")]
    [InlineData("/refs/Plant", "no drive")]
    [InlineData("refs/../secret", "no drive")]
    [InlineData("refs/NUL", "no drive")]
    [InlineData("module=refs/Plant", "name a kind")]
    [InlineData("other-share:Plant", "does not list")]
    [InlineData("company-refs:", "needs a path")]
    [InlineData("", "needs a path")]
    public void Parse_BadInputs_AreViolations(string given, string reason)
    {
        var violations = new List<string>();

        Assert.Null(PackInputs.Parse("inputs[0]", given, _config, violations));
        Assert.Contains(reason, Assert.Single(violations), StringComparison.Ordinal);
        Assert.DoesNotContain("server", violations[0], StringComparison.Ordinal);
    }
}

/// <summary>Resolving inputs below the import root and a pack source, in temporary folders.</summary>
[Trait("Size", "Medium")]
public sealed class PackInputsResolveTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-inputs-").FullName;

    private HostDataLocations Locations => new(_root);

    private HostConfig Config => HostConfig.Defaults(Locations) with
    {
        PackSources = new Dictionary<string, string> { ["company-refs"] = Path.Combine(_root, "share") },
    };

    [Fact]
    public void Resolve_FolderInImports_GivesItsPathWithoutARoot()
    {
        Directory.CreateDirectory(Path.Combine(_root, "imports", "refs", "Plant"));

        ScanInput input = PackInputs.Resolve("inputs[0]", new PackInputArgument(null, null, "refs/Plant"), Locations, Config, bundleProjects: true);

        Assert.Equal((Path.Combine(_root, "imports", "refs", "Plant"), true, null), (input.Path, input.BundleProjects, input.Root));
    }

    [Fact]
    public void Resolve_FileBelowAPackSource_NamesTheRootAndThePathBelowIt()
    {
        Directory.CreateDirectory(Path.Combine(_root, "share", "Projects"));
        File.WriteAllText(Path.Combine(_root, "share", "Projects", "Plant.elk"), "Plant.edb");

        ScanInput input = PackInputs.Resolve("inputs[0]", new PackInputArgument(null, "company-refs", "Projects/Plant.elk"), Locations, Config, bundleProjects: false);

        Assert.Equal(new ResourceReference { Root = "company-refs", Path = "Projects/Plant.elk" }, input.Root);
    }

    [Fact]
    public void Resolve_Missing_IsFileNotFoundWithoutTheRootsPath()
    {
        var ex = Assert.Throws<ImportFileException>(() => PackInputs.Resolve("inputs[0]", new PackInputArgument(null, null, "refs/Plant"), Locations, Config, false));

        Assert.Equal(ErrorReason.FileNotFound, ex.Error.Reason);
        Assert.DoesNotContain(_root, ex.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(Path.Combine(_root, "imports")));
    }

    [Fact]
    public void Resolve_ThroughAJunction_IsRefused()
    {
        Directory.CreateDirectory(Path.Combine(_root, "elsewhere", "Plant"));
        Directory.CreateDirectory(Path.Combine(_root, "imports"));
        using (var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{Path.Combine(_root, "imports", "refs")}\" \"{Path.Combine(_root, "elsewhere")}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!)
        {
            mklink.WaitForExit();
        }

        var ex = Assert.Throws<ImportFileException>(() => PackInputs.Resolve("inputs[0]", new PackInputArgument(null, null, "refs/Plant"), Locations, Config, false));

        Assert.Equal(ErrorReason.InvalidArgument, ex.Error.Reason);
        Assert.Contains("junction", ex.Error.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        string link = Path.Combine(_root, "imports", "refs");
        if (Directory.Exists(link) && new DirectoryInfo(link).Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            Directory.Delete(link);
        }

        Directory.Delete(_root, recursive: true);
    }
}
