using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>The page name format of eplan_page_create (tool spec section 2).</summary>
[Trait("Size", "Small")]
public sealed class PageNamesTests
{
    [Theory]
    [InlineData("=GAA+A1&EFS1/5", "GAA", "A1", "EFS1", 5)]
    [InlineData("+A1/12", null, "A1", null, 12)]
    [InlineData("=电源&EFS1/1", "电源", null, "EFS1", 1)]
    [InlineData("/7", null, null, null, 7)]
    [InlineData("=GAA.1+A1-2/2147483647", "GAA.1", "A1-2", null, int.MaxValue)]
    public void Parse_SplitsAValidNameIntoItsParts(string name, string? plant, string? location, string? documentType, int counter)
    {
        PageName? page = PageNames.Parse(name, out string? problem);

        Assert.Null(problem);
        Assert.Equal((name, plant, location, documentType, counter), (page!.Text, page.Plant, page.Location, page.DocumentType, page.Counter));
    }

    [Theory]
    [InlineData(null, "is required")]
    [InlineData("", "is required")]
    [InlineData("==GAA+A1/5", "'=='")]
    [InlineData("=GAA++A1/5", "'++'")]
    [InlineData("=GAA#X/5", "'#'")]
    [InlineData("#X/5", "'#'")]
    [InlineData("=/5", "empty value")]
    [InlineData("=GAA+/5", "empty value")]
    [InlineData("=GA A/5", "white space")]
    [InlineData("=GAA\t/5", "white space")]
    [InlineData("=GAA\u0001/5", "control character")]
    [InlineData(" =GAA/5", "must start with")]
    [InlineData("GAA/5", "must start with")]
    [InlineData("+A1=GAA/5", "order")]
    [InlineData("=GAA=GAB/5", "order")]
    [InlineData("=GAA+A1&EFS1", "needs '/'")]
    [InlineData("=GAA/", "page number")]
    [InlineData("=GAA/0", "page number")]
    [InlineData("=GAA/05", "page number")]
    [InlineData("=GAA/5a", "page number")]
    [InlineData("=GAA/-5", "page number")]
    [InlineData("=GAA/5/6", "page number")]
    [InlineData("=GAA/2147483648", "page number")]
    public void Parse_RejectsWithTheReason(string? name, string reason)
    {
        Assert.Null(PageNames.Parse(name, out string? problem));
        Assert.Contains(reason, problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("=GAA+A1&EFS1/5", "=GAA+A1&EFS1")]
    [InlineData("/5", "")]
    [InlineData("=A/1.1/2", "=A/1.1")]
    [InlineData("=A", "=A")]
    public void StructureOf_IsEverythingBeforeTheLastSlash(string name, string structure)
    {
        Assert.Equal(structure, PageNames.StructureOf(name));
    }
}
