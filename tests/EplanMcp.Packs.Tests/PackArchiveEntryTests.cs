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
        Assert.Contains("only the case or the Unicode normalisation differs", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckEntries_NameThatIsFileAndFolder_IsRefused()
    {
        using ZipArchive zip = Open(new Entry("pack.json"), new Entry("res/a"), new Entry("res/a/b.ema"));

        Assert.Equal(new PackProblem("res/a", "is both a file and a folder"), Assert.Single(PackArchive.CheckEntries(zip, PackLimits.Default)));
    }

    [Fact]
    public void CheckEntries_EntryCompressedMoreThan200To1_IsRefused()
    {
        using ZipArchive zip = Open(new Entry("pack.json"), new Entry("resources/docs/zeros.txt", new string('0', 100_000)));

        Assert.Equal(new PackProblem("resources/docs/zeros.txt", "is compressed more than 200:1"), Assert.Single(PackArchive.CheckEntries(zip, PackLimits.Default)));
    }

    [Fact]
    public void CheckEntries_NamesEqualInNfc_AreRefused()
    {
        using ZipArchive zip = Open(new Entry("pack.json"), new Entry("docs/café.md"), new Entry("docs/café.md"));

        PackProblem problem = Assert.Single(PackArchive.CheckEntries(zip, PackLimits.Default));

        Assert.Equal("docs/café.md", problem.Location);
        Assert.Contains("Unicode normalisation", problem.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tools/run.ps1")]
    [InlineData("resources/macros/Motor.EXE")]
    [InlineData("scripts/Action.cs")]
    [InlineData("setup.msi")]
    public void CheckEntries_ExecutableType_IsRefused(string name)
    {
        using ZipArchive zip = Open(new Entry("pack.json"), new Entry(name));

        Assert.Equal(new PackProblem(name, "is an executable or script type, which packs never hold"), Assert.Single(PackArchive.CheckEntries(zip, PackLimits.Default)));
    }

    [Fact]
    public void Limits_AboveAdr0024_AreCapped()
    {
        PackLimits capped = new PackLimits { MaxEntries = 50_000, MaxCompressionRatio = 1000, MaxTotalBytes = 1024 }.Capped;

        Assert.Equal((20_000, 200, 1024L, 2L << 30), (capped.MaxEntries, capped.MaxCompressionRatio, capped.MaxTotalBytes, capped.MaxArchiveBytes));
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
