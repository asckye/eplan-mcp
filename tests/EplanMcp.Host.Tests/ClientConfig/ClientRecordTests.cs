using EplanMcp.Host.ClientConfig;

namespace EplanMcp.Host.Tests.ClientConfig;

/// <summary>The record of written entries, <c>state\clients.json</c>, which the uninstaller removes by (design 4.3).</summary>
[Trait("Size", "Medium")]
public sealed class ClientRecordTests : IDisposable
{
    private static readonly DateTimeOffset _time = new(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);

    private readonly string _home = Directory.CreateTempSubdirectory("eplanmcp-数据-").FullName;

    private ClientRecord Record => ClientRecord.For(new HostDataLocations(_home));

    public void Dispose() => Directory.Delete(_home, recursive: true);

    [Fact]
    public void All_WithoutARecord_IsEmpty()
    {
        Assert.Empty(Record.All());
        Assert.Equal(Path.Combine(_home, "state", "clients.json"), Record.FilePath);
    }

    [Fact]
    public void Add_WritesTheDocumentedShape()
    {
        Record.Add(new("codex", @"C:\Users\工程师\.codex\config.toml", "eplan", _time));

        Assert.Equal(
            """
            {
              "schemaVersion": 1,
              "entries": [
                {
                  "client": "codex",
                  "file": "C:\\Users\\\u5DE5\u7A0B\u5E08\\.codex\\config.toml",
                  "entry": "eplan",
                  "time": "2026-10-21T08:00:00+00:00"
                }
              ]
            }
            """,
            File.ReadAllText(Record.FilePath).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Add_OfTheSameEntryAgain_ReplacesTheEarlierOne()
    {
        Record.Add(new("codex", @"C:\U\.codex\config.toml", "eplan", _time));
        Record.Add(new("cursor", @"C:\U\.cursor\mcp.json", "eplan", _time));
        Record.Add(new("codex", @"c:\u\.CODEX\config.toml", "eplan", _time.AddHours(1)));
        Record.Add(new("codex", @"C:\U\.codex\config.toml", "eplan-vm", _time));

        Assert.Equal(
            [
                new("cursor", @"C:\U\.cursor\mcp.json", "eplan", _time),
                new("codex", @"c:\u\.CODEX\config.toml", "eplan", _time.AddHours(1)),
                new RecordedEntry("codex", @"C:\U\.codex\config.toml", "eplan-vm", _time),
            ],
            Record.All());
    }

    [Fact]
    public void Remove_TakesOutOnlyThatEntry()
    {
        Record.Add(new("codex", @"C:\U\.codex\config.toml", "eplan", _time));
        Record.Add(new("codex", @"C:\U\.codex\config.toml", "eplan-vm", _time));
        Record.Add(new("cursor", @"C:\U\.cursor\mcp.json", "eplan", _time));

        Record.Remove("codex", @"C:\U\.CODEX\config.toml", "eplan");
        Record.Remove("gemini-cli", @"C:\U\.gemini\settings.json", "eplan");

        Assert.Equal(["eplan-vm", "eplan"], Record.All().Select(e => e.Entry));
        Assert.Equal(["codex", "cursor"], Record.All().Select(e => e.Client));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"schemaVersion": 2, "entries": []}""")]
    [InlineData("""{"schemaVersion": 1}""")]
    [InlineData("""{"schemaVersion": 1, "entries": [null]}""")]
    [InlineData("""{"schemaVersion": 1, "entries": [{"client": "codex", "file": null, "entry": "eplan", "time": "2026-10-21T08:00:00+00:00"}]}""")]
    [InlineData("""{"schemaVersion": 1, "entries": [{"client": "codex", "entry": "eplan", "time": "2026-10-21T08:00:00+00:00"}]}""")]
    public void AnUnreadableRecord_IsAnErrorAndIsNotOverwritten(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Record.FilePath)!);
        File.WriteAllText(Record.FilePath, text);

        Assert.Equal(ClientProblem.InvalidRecord, Assert.Throws<ClientConfigException>(() => Record.All()).Problem);
        Assert.Throws<ClientConfigException>(() => Record.Add(new("codex", @"C:\U\.codex\config.toml", "eplan", _time)));
        Assert.Equal(text, File.ReadAllText(Record.FilePath));
    }
}
