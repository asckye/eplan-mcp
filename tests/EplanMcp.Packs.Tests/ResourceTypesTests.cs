namespace EplanMcp.Packs.Tests;

/// <summary>The allow-list of ADR-0024 section 1 with its revision of 2026-10-21 (XLSX, PDF, DOCX stored, never parsed).</summary>
[Trait("Size", "Small")]
public sealed class ResourceTypesTests
{
    [Theory]
    [InlineData("resources/docs/parts.xlsx")]
    [InlineData("resources/docs/Manual.PDF")]
    [InlineData("resources/docs/guide.docx")]
    [InlineData("resources/docs/readme.md")]
    public void Documents_MayBeOfficeAndPdfFiles(string path)
    {
        Assert.Null(ResourceTypes.Problem(ResourceKind.Document, path));
    }

    [Theory]
    [InlineData(ResourceKind.MaterialMapping, "resources/mappings/parts.xlsx")]
    [InlineData(ResourceKind.Rule, "resources/rules/rules.docx")]
    public void OtherKinds_AreNoOfficeFiles(ResourceKind kind, string path)
    {
        Assert.Contains("may not have", ResourceTypes.Problem(kind, path), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("resources/docs/parts.xlsm")]
    [InlineData("resources/docs/parts.XLTM")]
    [InlineData("resources/docs/guide.docm")]
    [InlineData("resources/docs/guide.dotm")]
    [InlineData("resources/docs/slides.pptm")]
    [InlineData("resources/docs/slides.potm")]
    public void MacroEnabledOfficeFiles_AreForbiddenForEveryKind(string path)
    {
        Assert.All(Enum.GetValues<ResourceKind>(), kind => Assert.Contains("macro-enabled Office file", ResourceTypes.Problem(kind, path), StringComparison.Ordinal));
        Assert.NotNull(ResourceTypes.Forbidden(path));
    }
}
