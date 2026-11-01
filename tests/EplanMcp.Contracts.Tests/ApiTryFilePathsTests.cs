namespace EplanMcp.Contracts.Tests;

[Trait("Size", "Small")]
public sealed class ApiTryFilePathsTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("C:\\outside.xml")]
    [InlineData("C:outside.xml")]
    [InlineData("\\\\server\\share\\file.xml")]
    [InlineData("//server/share/file.xml")]
    [InlineData("\\file.xml")]
    [InlineData("/file.xml")]
    [InlineData("../file.xml")]
    [InlineData("a/../file.xml")]
    [InlineData("a/./file.xml")]
    [InlineData("a//file.xml")]
    [InlineData("a\\\\file.xml")]
    [InlineData("file.xml:stream")]
    [InlineData("a./file.xml")]
    [InlineData("a /file.xml")]
    [InlineData("file.xml.")]
    [InlineData("file.xml ")]
    [InlineData("CON.xml")]
    [InlineData("con .xml")]
    [InlineData("NUL")]
    [InlineData("COM0.xml")]
    [InlineData("LPT³.xml")]
    [InlineData("a/<file.xml")]
    [InlineData("a/?file.xml")]
    [InlineData("a/*file.xml")]
    [InlineData("a/fi\nle.xml")]
    public void EscapesAndWindowsAliasesAreRejected(string file)
    {
        Assert.False(ApiTryFilePaths.Valid(file, false));
        Assert.False(ApiTryFilePaths.Valid(file, true));
        Assert.Throws<ArgumentException>(() => ApiTryFilePaths.Resolve(@"D:\Host", new string('a', 32), file, false));
    }

    [Fact]
    public void InputPathsAndOutputNamesHaveSeparateLengthAndShapeBounds()
    {
        Assert.True(ApiTryFilePaths.Valid("drawings/input.xml", false));
        Assert.True(ApiTryFilePaths.Valid("drawings\\input.xml", false));
        Assert.False(ApiTryFilePaths.Valid("drawings/output.xml", true));
        Assert.True(ApiTryFilePaths.Valid("output.xml", true));
        Assert.False(ApiTryFilePaths.Valid("output..xml", true));
        Assert.True(ApiTryFilePaths.Valid("input..xml", false));
        Assert.True(ApiTryFilePaths.Valid(new string('a', 260), false));
        Assert.False(ApiTryFilePaths.Valid(new string('a', 261), false));
        Assert.True(ApiTryFilePaths.Valid(new string('a', 100), true));
        Assert.False(ApiTryFilePaths.Valid(new string('a', 101), true));
        Assert.Throws<ArgumentException>(() => ApiTryFilePaths.Resolve(@"\\server\share", new string('a', 32), "input.xml", false));
        Assert.Throws<ArgumentException>(() => ApiTryFilePaths.Resolve(@"D:\Host", "../escape", "output.xml", true));
        Assert.Throws<ArgumentException>(() => ApiTryFilePaths.Resolve(@"D:\Host", new string('a', 32), new string('a', 260), false));
    }
}
