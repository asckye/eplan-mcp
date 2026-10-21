using EplanMcp.Host.Addin;

namespace EplanMcp.Host.Tests.Addin;

/// <summary>Removing the download mark (the Zone.Identifier stream) as Unblock-File does, on the temporary NTFS folder. Medium.</summary>
[Trait("Size", "Medium")]
public sealed class ZoneMarkTests : IDisposable
{
    private const string Mark = "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.invalid/EplanMcp-setup.exe\r\n";

    private readonly string _folder = Directory.CreateTempSubdirectory("eplanmcp-zone-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void RemoveAll_TakesTheMarkOffEveryFileBelowAndLeavesTheContents()
    {
        string dll = Put("net481", "EplanMcp.Bridge.Eplan.dll", marked: true);
        string deep = Put(Path.Combine("net481", "zh-CN"), "EplanMcp.Bridge.Eplan.resources.dll", marked: true);
        string clean = Put("net481", "EplanMcp.Contracts.dll", marked: false);
        Assert.Equal(Mark, File.ReadAllText(dll + ZoneMark.Stream));

        ZoneMark.RemoveAll(_folder);

        Assert.All(new[] { dll, deep, clean }, f => Assert.False(File.Exists(f + ZoneMark.Stream), f));
        Assert.All(new[] { dll, deep, clean }, f => Assert.Equal(Path.GetFileName(f), File.ReadAllText(f)));
    }

    [Fact]
    public void RemoveAll_OnAnUnmarkedFolder_ChangesNothing()
    {
        string dll = Put("net481", "EplanMcp.Bridge.Eplan.dll", marked: false);
        DateTime written = File.GetLastWriteTimeUtc(dll);

        ZoneMark.RemoveAll(_folder);
        ZoneMark.RemoveAll(_folder);

        Assert.Equal(written, File.GetLastWriteTimeUtc(dll));
    }

    private string Put(string folder, string name, bool marked)
    {
        string file = Path.Combine(_folder, folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, name);
        if (marked)
        {
            File.WriteAllText(file + ZoneMark.Stream, Mark);
        }

        return file;
    }
}
