using System.Buffers.Text;
using System.Text;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class PageCursorTests
{
    private static readonly string _pages = PageCursor.Fingerprint("eplan_page_list", "Line1", "=AP");

    [Fact]
    public void Encode_ThenDecode_WithTheSameFingerprint_ReturnsTheOffset()
    {
        Assert.Equal(42, PageCursor.Decode(PageCursor.Encode(42, _pages), _pages));
    }

    [Fact]
    public void Decode_WithOtherFiltersOrAnotherTool_IsRejected()
    {
        string cursor = PageCursor.Encode(42, _pages);

        Assert.Null(PageCursor.Decode(cursor, PageCursor.Fingerprint("eplan_page_list", "Line1", "=EB")));
        Assert.Null(PageCursor.Decode(cursor, PageCursor.Fingerprint("eplan_device_list", "Line1", "=AP")));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("own", 42)]
    public void Offset_WithoutOrWithItsOwnCursor_IsTheOffsetWithoutAViolation(string? cursor, int expected)
    {
        var violations = new List<string>();

        int offset = PageCursor.Offset(cursor == "own" ? PageCursor.Encode(42, _pages) : cursor, _pages, "eplan_page_list", violations);

        Assert.Equal(expected, offset);
        Assert.Empty(violations);
    }

    [Fact]
    public void Offset_OfAnotherListing_IsZeroWithAViolationNamingTheTool()
    {
        var violations = new List<string>();

        int offset = PageCursor.Offset(PageCursor.Encode(42, _pages), PageCursor.Fingerprint("eplan_page_list", "Line2", "=AP"), "eplan_page_list", violations);

        Assert.Equal(0, offset);
        Assert.Contains("'cursor' was not returned by eplan_page_list", Assert.Single(violations), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-a-cursor")]
    [InlineData("")]
    [InlineData("djE6NQ")] // "v1:5", what the M1 Host produced
    public void Decode_MalformedOrOldCursor_IsRejected(string cursor)
    {
        Assert.Null(PageCursor.Decode(cursor, _pages));
    }

    [Fact]
    public void Decode_TamperedOffset_IsRejected()
    {
        string tampered = Base64Url.EncodeToString(Encoding.ASCII.GetBytes("v2:-1:" + _pages));

        Assert.Null(PageCursor.Decode(tampered, _pages));
    }

    [Fact]
    public void Fingerprint_SeparatesTheParts()
    {
        Assert.Equal(PageCursor.Fingerprint("t", "a", "bc"), PageCursor.Fingerprint("t", "a", "bc"));
        Assert.NotEqual(PageCursor.Fingerprint("t", "a", "bc"), PageCursor.Fingerprint("t", "ab", "c"));
        Assert.NotEqual(PageCursor.Fingerprint("t", (string?)null), PageCursor.Fingerprint("t", string.Empty));
    }
}
