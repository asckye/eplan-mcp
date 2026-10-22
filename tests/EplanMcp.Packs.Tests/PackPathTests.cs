namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class PackPathTests
{
    [Theory]
    [InlineData("resources/macros/Motor.ema")]
    [InlineData("resources/项目/示例.elk")]
    [InlineData("a")]
    public void Problem_ValidPath_IsNull(string path)
    {
        Assert.Null(PackPath.Problem(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/rooted")]
    [InlineData("trailing/")]
    [InlineData("a//b")]
    [InlineData("../escape")]
    [InlineData("a/../../escape")]
    [InlineData("./a")]
    [InlineData("a\\b")]
    [InlineData("C:/Windows/x")]
    [InlineData("C:x")]
    [InlineData("//server/share/x")]
    [InlineData("a/file.txt:stream")]
    [InlineData("a/nul")]
    [InlineData("a/COM1.txt")]
    [InlineData("a/name.")]
    [InlineData("a/name ")]
    [InlineData("a/b*c")]
    [InlineData("a/b\u0001c")]
    public void Problem_UnsafePath_IsNamed(string path)
    {
        Assert.NotNull(PackPath.Problem(path));
    }

    [Fact]
    public void Problem_TooLong_IsNamed()
    {
        Assert.NotNull(PackPath.Problem(new string('a', PackPath.MaxLength + 1)));
    }
}
