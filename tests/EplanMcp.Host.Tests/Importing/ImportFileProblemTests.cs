using EplanMcp.Host.Importing;

namespace EplanMcp.Host.Tests.Importing;

/// <summary>SEC-31: the form of an import tool's file argument, checked before the disk is read (SEC-19's cases).</summary>
[Trait("Size", "Small")]
public sealed class ImportFileProblemTests
{
    private static readonly string[] _drawings = [".dxf", ".dwg"];

    [Theory]
    [InlineData("4685200F.DXF")]
    [InlineData("drawings/4685200F.dxf")]
    [InlineData(@"drawings\Rittal\plan.dwg")]
    [InlineData("  drawings/plan.dxf  ")]
    [InlineData("电气/图纸.dxf")]
    public void APathBelowTheRoot_WithTheToolsExtension_IsFine(string file)
    {
        Assert.Null(ImportFiles.Problem("file", file, _drawings));
    }

    [Theory]
    [InlineData(@"C:\drawings\plan.dxf")]
    [InlineData("C:plan.dxf")]
    [InlineData(@"\\server\share\plan.dxf")]
    [InlineData("//server/share/plan.dxf")]
    [InlineData("/plan.dxf")]
    [InlineData(@"\plan.dxf")]
    [InlineData("../plan.dxf")]
    [InlineData(@"drawings\..\..\plan.dxf")]
    [InlineData("./plan.dxf")]
    [InlineData("drawings//plan.dxf")]
    [InlineData("drawings/")]
    [InlineData(".. /plan.dxf")]
    [InlineData("drawings./plan.dxf")]
    [InlineData("plan.dxf:hidden")]
    [InlineData("CON.dxf")]
    [InlineData("drawings/nul/plan.dxf")]
    [InlineData("draw*ings/plan.dxf")]
    [InlineData("drawings/pl\u0001an.dxf")]
    public void APathThatCouldLeaveTheRoot_OrNameADevice_IsRefused(string file)
    {
        string? problem = ImportFiles.Problem("file", file, _drawings);

        Assert.NotNull(problem);
        Assert.StartsWith("'file' must be a path below the 'imports' folder", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void AMissingFile_IsRequired_WithAnExample(string? file)
    {
        Assert.Equal(
            "'file' is required: a path below the 'imports' folder in EplanMcp's data folder, such as drawings/plan.dxf.",
            ImportFiles.Problem("file", file, _drawings));
    }

    [Fact]
    public void AnotherExtension_NamesTheToolsOnes()
    {
        Assert.Equal("'file' must end with .dxf or .dwg.", ImportFiles.Problem("file", "drawings/plan.pdf", _drawings));
    }

    [Fact]
    public void ALongPath_IsRefused()
    {
        string folder = new('a', 200);
        Assert.Equal("'file' must have at most 260 characters.", ImportFiles.Problem("file", folder + "/" + new string('b', 56) + ".dxf", _drawings));
        Assert.Null(ImportFiles.Problem("file", folder + "/" + new string('b', 55) + ".dxf", _drawings));
    }

    [Fact]
    public void Normalized_HasSlashesBetweenFolders()
    {
        Assert.Equal("drawings/Rittal/plan.DXF", ImportFiles.Normalized(@" drawings\Rittal/plan.DXF "));
    }
}
