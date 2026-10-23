using System.Text;
using EplanMcp.Host.ClientConfig;

namespace EplanMcp.Host.Tests.ClientConfig;

/// <summary>
/// Writing a client's file: the timestamped backup, the refusal when the file changed after it was read, and the
/// failure path. Medium: the files are in a temporary directory, never the user's own.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ClientFileChangeTests : IDisposable
{
    private static readonly DateTimeOffset _now = new DateTimeOffset(2026, 10, 21, 8, 15, 30, TimeSpan.FromHours(8)).AddTicks(1234567);

    private readonly string _folder = Directory.CreateTempSubdirectory("eplanmcp-client-").FullName;

    private string FilePath => Path.Combine(_folder, "客户端", "mcp.json");

    public void Dispose()
    {
        foreach (string file in Directory.EnumerateFiles(_folder, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Read_WithoutAFile_IsEmpty()
    {
        Assert.Equal(string.Empty, ClientFileChange.Read(FilePath).Text);
    }

    [Fact]
    public void Read_DropsAByteOrderMark()
    {
        WriteBytes([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("{\"中\": 1}")]);

        Assert.Equal("{\"中\": 1}", ClientFileChange.Read(FilePath).Text);
    }

    [Fact]
    public void Read_OfBytesThatAreNotUtf8_IsRefused()
    {
        WriteBytes([0x7B, 0xC4, 0xE3, 0x7D]);

        Assert.Equal(ClientProblem.InvalidFile, Assert.Throws<ClientConfigException>(() => ClientFileChange.Read(FilePath)).Problem);
    }

    [Fact]
    public void Read_OfASymbolicLink_IsRefused()
    {
        string target = Path.Combine(_folder, "dotfiles.json");
        File.WriteAllText(target, "{}");
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        try
        {
            File.CreateSymbolicLink(FilePath, target);
        }
        catch (IOException ex)
        {
            Assert.Skip($"Creating a symbolic link needs developer mode or elevation here: {ex.Message}");
        }

        ClientConfigException refused = Assert.Throws<ClientConfigException>(() => ClientFileChange.Read(FilePath));

        Assert.Equal(ClientProblem.InvalidFile, refused.Problem);
        Assert.Contains(target, refused.Detail, StringComparison.Ordinal);
        Assert.Equal("{}", File.ReadAllText(target));
    }

    [Fact]
    public void Apply_ToANewFile_CreatesItWithoutABackup()
    {
        string? backup = ClientFileChange.Read(FilePath).Apply("{\"中\": 1}\n", _now);

        Assert.Null(backup);
        Assert.Equal(Encoding.UTF8.GetBytes("{\"中\": 1}\n"), File.ReadAllBytes(FilePath));
        Assert.Equal([FilePath], Directory.GetFiles(Path.GetDirectoryName(FilePath)!));
    }

    [Fact]
    public void Apply_ToAnExistingFile_BacksItUpWithTheUtcTimeFirst()
    {
        WriteBytes(Encoding.UTF8.GetBytes("old"));

        string? backup = ClientFileChange.Read(FilePath).Apply("new", _now);

        Assert.Equal(FilePath + ".bak_20261021T0015301234567Z", backup);
        Assert.Equal("old", File.ReadAllText(backup!));
        Assert.Equal("new", File.ReadAllText(FilePath));
        Assert.Equal([FilePath, backup], Directory.GetFiles(Path.GetDirectoryName(FilePath)!).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("old", "changed")]
    [InlineData("old", null)]
    [InlineData(null, "created")]
    public void Apply_AfterTheFileChanged_IsRefusedAndTouchesNothing(string? before, string? after)
    {
        if (before is not null)
        {
            WriteBytes(Encoding.UTF8.GetBytes(before));
        }

        ClientFileChange change = ClientFileChange.Read(FilePath);
        if (after is null)
        {
            File.Delete(FilePath);
        }
        else
        {
            WriteBytes(Encoding.UTF8.GetBytes(after));
        }

        ClientConfigException refused = Assert.Throws<ClientConfigException>(() => change.Apply("ours", _now));

        Assert.Equal(ClientProblem.FileChanged, refused.Problem);
        Assert.Equal(after is null ? [] : [FilePath], Directory.GetFiles(Path.GetDirectoryName(FilePath)!));
        Assert.Equal(after, File.Exists(FilePath) ? File.ReadAllText(FilePath) : null);
    }

    [Fact]
    public void Apply_ThatFails_KeepsTheOriginalAndTheBackup()
    {
        WriteBytes(Encoding.UTF8.GetBytes("old"));
        File.SetAttributes(FilePath, FileAttributes.ReadOnly);
        ClientFileChange change = ClientFileChange.Read(FilePath);

        Assert.ThrowsAny<Exception>(() => change.Apply("new", _now));

        Assert.Equal("old", File.ReadAllText(FilePath));
        string backup = FilePath + ".bak_20261021T0015301234567Z";
        Assert.Equal("old", File.ReadAllText(backup));
        Assert.Equal([FilePath, backup], Directory.GetFiles(Path.GetDirectoryName(FilePath)!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Apply_TwiceWithinOneClockTick_NeverOverwritesTheEarlierBackup()
    {
        WriteBytes(Encoding.UTF8.GetBytes("old"));
        File.SetAttributes(FilePath, FileAttributes.ReadOnly);
        ClientFileChange change = ClientFileChange.Read(FilePath);

        Assert.ThrowsAny<Exception>(() => change.Apply("new", _now));
        Assert.ThrowsAny<Exception>(() => change.Apply("new", _now));

        string[] backups = Directory.GetFiles(Path.GetDirectoryName(FilePath)!, "*.bak_*");
        Assert.Equal(2, backups.Length);
        Assert.All(backups, backup => Assert.Equal("old", File.ReadAllText(backup)));
        Assert.Equal("old", File.ReadAllText(FilePath));
    }

    private void WriteBytes(byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllBytes(FilePath, bytes);
    }
}
