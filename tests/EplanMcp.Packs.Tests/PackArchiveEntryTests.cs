using System.IO.Compression;
using static EplanMcp.Packs.Tests.Zips;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class PackArchiveEntryTests
{
    [Fact]
    public void CheckEntries_SafeArchive_HasNoProblems()
    {
        using ZipArchive zip = Open(new Entry("pack.json"), new Entry("resources/"), new Entry("resources/项目/示例.elk"), new Entry("resources/a.ema"));

        Assert.Empty(PackArchive.CheckEntries(zip, PackLimits.Default));
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("resources/../../evil.txt")]
    [InlineData("/rooted.txt")]
    [InlineData("C:/Windows/evil.txt")]
    [InlineData("C:evil.txt")]
    [InlineData("resources\\evil.txt")]
    [InlineData("..\\evil.txt")]
    [InlineData("//server/share/evil.txt")]
    [InlineData("resources/nul")]
    [InlineData("resources/CON.txt")]
    [InlineData("resources/evil.")]
    [InlineData("resources/file.txt:stream")]
    [InlineData("../")]
    public void CheckEntries_UnsafeName_IsRefused(string name)
    {
        using ZipArchive zip = Open(new Entry("pack.json"), new Entry(name));

        PackProblem problem = Assert.Single(PackArchive.CheckEntries(zip, PackLimits.Default));

        Assert.Equal(name, problem.Location);
    }

    [Theory]
    [InlineData(unchecked((int)0xA1FF0000), "is a symbolic link or reparse point")]
    [InlineData(0x400, "is a symbolic link or reparse point")]
    public void CheckEntries_Link_IsRefused(int attributes, string message)
    {
        using ZipArchive zip = Open(new Entry("pack.json"), new Entry("resources/link", "C:/Windows", attributes));

        Assert.Equal(new PackProblem("resources/link", message), Assert.Single(PackArchive.CheckEntries(zip, PackLimits.Default)));
    }

    [Fact]
    public void CheckEntries_DuplicateName_IsRefused()
    {
        using ZipArchive zip = Open(new Entry("pack.json"), new Entry("a.ema", "1"), new Entry("a.ema", "2"));

        Assert.Equal(new PackProblem("a.ema", "is in the archive twice"), Assert.Single(PackArchive.CheckEntries(zip, PackLimits.Default)));
    }

    [Fact]
    public void CheckEntries_NamesDifferingOnlyInCase_AreRefused()
    {
        using ZipArchive zip = Open(new Entry("pack.json"), new Entry("res/Motor.ema"), new Entry("RES/motor.EMA"));

        PackProblem problem = Assert.Single(PackArchive.CheckEntries(zip, PackLimits.Default));

        Assert.Equal("RES/motor.EMA", problem.Location);
        Assert.Contains("only the case differs", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckEntries_NameThatIsFileAndFolder_IsRefused()
    {
        using ZipArchive zip = Open(new Entry("pack.json"), new Entry("res/a"), new Entry("res/a/b.ema"));

        Assert.Equal(new PackProblem("res/a", "is both a file and a folder"), Assert.Single(PackArchive.CheckEntries(zip, PackLimits.Default)));
    }

    [Fact]
    public void CheckEntries_TooManyEntries_IsRefused()
    {
        using ZipArchive zip = Open(new Entry("pack.json"), new Entry("a"), new Entry("b"));

        Assert.Equal("archive", Assert.Single(PackArchive.CheckEntries(zip, new PackLimits { MaxEntries = 2 })).Location);
    }

    [Fact]
    public void CheckEntries_EntryOrTotalTooLarge_IsRefused()
    {
        using ZipArchive zip = Open(new Entry("pack.json", "12345"), new Entry("big", "1234567890"));

        Assert.Equal("big", Assert.Single(PackArchive.CheckEntries(zip, new PackLimits { MaxEntryBytes = 8 })).Location);
        Assert.Equal("archive", Assert.Single(PackArchive.CheckEntries(zip, new PackLimits { MaxTotalBytes = 12 })).Location);
    }
}
