using EplanMcp.Host.Exports;

namespace EplanMcp.Host.Tests.Exports;

/// <summary>The file_name rules of eplan_export_pdf (tool spec 2, SEC-20) and the sizes summaries show.</summary>
[Trait("Size", "Small")]
public sealed class ExportFileNameTests
{
    [Theory]
    [InlineData("Line1")]
    [InlineData("Line1 Schaltplan 3.pdf")]
    [InlineData("电气图纸")]
    [InlineData("a.b")]
    [InlineData("CONSOLE")]
    [InlineData(null)]
    [InlineData("")]
    public void Problem_AcceptsAPlainFileName(string? fileName)
    {
        Assert.Null(ExportFileName.Problem(fileName));
    }

    [Theory]
    [InlineData(@"..\x", "'\\' or '/'")]
    [InlineData("../x", "'\\' or '/'")]
    [InlineData(@"\\server\share\x", "'\\' or '/'")]
    [InlineData("C:x", "drive letter")]
    [InlineData("x.pdf:stream", "drive letter")]
    [InlineData("a..b", "'..'")]
    [InlineData("a\tb", "control characters")]
    [InlineData("a?b", "control characters")]
    [InlineData("a<b", "control characters")]
    [InlineData("report.", "end with a dot")]
    [InlineData("report ", "end with a dot or a space")]
    [InlineData("NUL", "reserves for devices")]
    [InlineData("com1.pdf", "reserves for devices")]
    [InlineData("LPT¹", "reserves for devices")]
    public void Problem_NamesWhatIsWrong(string fileName, string text)
    {
        Assert.Contains(text, ExportFileName.Problem(fileName), StringComparison.Ordinal);
    }

    [Fact]
    public void Problem_RefusesMoreThan100Characters()
    {
        Assert.Null(ExportFileName.Problem(new string('a', ExportFileName.MaxLength)));
        Assert.Contains("at most 100", ExportFileName.Problem(new string('a', ExportFileName.MaxLength + 1)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Line1", "Proj", "Line1.pdf")]
    [InlineData("Line1.PDF", "Proj", "Line1.PDF")]
    [InlineData("a.b", "Proj", "a.b.pdf")]
    [InlineData(null, "Line1", "Line1.pdf")]
    [InlineData("", "a/b", "a_b.pdf")]
    public void Pdf_AddsTheExtensionUnlessThere_AndDefaultsToTheProject(string? fileName, string project, string expected)
    {
        Assert.Equal(expected, ExportFileName.Pdf(fileName, project));
    }

    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(1023, "1023 bytes")]
    [InlineData(208_896, "204 KB")]
    [InlineData(249_856, "244 KB")]
    [InlineData(6_396_314, "6.1 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3 GB")]
    public void ByteSize_ShowsWholeKbAndOneDecimalAbove(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSize.Format(bytes));
    }
}
