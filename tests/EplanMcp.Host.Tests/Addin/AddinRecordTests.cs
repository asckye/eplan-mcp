using EplanMcp.Host.Addin;

namespace EplanMcp.Host.Tests.Addin;

/// <summary>The record of registrations, <c>state\addins.json</c>, which uninstall-addin undoes (design 4.2). Medium.</summary>
[Trait("Size", "Medium")]
public sealed class AddinRecordTests : IDisposable
{
    private const string Eplan = @"C:\Program Files\EPLAN\Platform\2026.0.3\Bin\EPLAN.exe";
    private const string Dll = @"C:\Users\工程师\EplanMcp\app\bridge\net481\EplanMcp.Bridge.Eplan.dll";
    private static readonly DateTimeOffset _time = new(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);

    private readonly string _home = Directory.CreateTempSubdirectory("eplanmcp-数据-").FullName;

    private AddinRecord Record => AddinRecord.For(new HostDataLocations(_home));

    public void Dispose() => Directory.Delete(_home, recursive: true);

    [Fact]
    public void All_WithoutARecord_IsEmpty()
    {
        Assert.Empty(Record.All());
        Assert.Equal(Path.Combine(_home, "state", "addins.json"), Record.FilePath);
    }

    [Fact]
    public void Add_WritesTheDocumentedShape()
    {
        Record.Add(new(Eplan, "2026.0.3.25421", "Electric P8", Dll, _time));

        Assert.Equal(
            """
            {
              "schemaVersion": 1,
              "registrations": [
                {
                  "eplan": "C:\\Program Files\\EPLAN\\Platform\\2026.0.3\\Bin\\EPLAN.exe",
                  "version": "2026.0.3.25421",
                  "variant": "Electric P8",
                  "dll": "C:\\Users\\\u5DE5\u7A0B\u5E08\\EplanMcp\\app\\bridge\\net481\\EplanMcp.Bridge.Eplan.dll",
                  "time": "2026-10-21T08:00:00+00:00"
                }
              ]
            }
            """,
            File.ReadAllText(Record.FilePath).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Add_ForTheSameEplanAndVariantAgain_ReplacesTheEarlierOne()
    {
        Record.Add(new(Eplan, "2026.0.3.25421", "Electric P8", Dll, _time));
        Record.Add(new(Eplan, "2026.0.3.25421", "Pro Panel", Dll, _time));
        Record.Add(new(Eplan.ToUpperInvariant(), "2026.0.3.25421", "electric p8", @"D:\dev\EplanMcp.Bridge.Eplan.dll", _time.AddHours(1)));

        Assert.Equal(
            [
                new(Eplan, "2026.0.3.25421", "Pro Panel", Dll, _time),
                new RecordedAddin(Eplan.ToUpperInvariant(), "2026.0.3.25421", "electric p8", @"D:\dev\EplanMcp.Bridge.Eplan.dll", _time.AddHours(1)),
            ],
            Record.All());
    }

    [Fact]
    public void Remove_TakesOutOnlyThatEplanAndVariant()
    {
        Record.Add(new(Eplan, "2026.0.3.25421", "Electric P8", Dll, _time));
        Record.Add(new(Eplan, "2026.0.3.25421", "Pro Panel", Dll, _time));

        Record.Remove(Eplan, "PRO PANEL");
        Record.Remove(@"C:\Program Files\EPLAN\Platform\2025.0.3\Bin\EPLAN.exe", "Electric P8");

        Assert.Equal(["Electric P8"], Record.All().Select(r => r.Variant));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"schemaVersion": 2, "registrations": []}""")]
    [InlineData("""{"schemaVersion": 1}""")]
    [InlineData("""{"schemaVersion": 1, "registrations": [null]}""")]
    [InlineData("""{"schemaVersion": 1, "registrations": [{"eplan": "E", "version": "1", "dll": "D", "time": "2026-10-21T08:00:00+00:00"}]}""")]
    public void AnUnreadableRecord_IsAnErrorAndIsNotOverwritten(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Record.FilePath)!);
        File.WriteAllText(Record.FilePath, text);

        Assert.Equal(AddinProblem.InvalidRecord, Assert.Throws<AddinException>(() => Record.All()).Problem);
        Assert.Throws<AddinException>(() => Record.Add(new(Eplan, "2026.0.3.25421", "Electric P8", Dll, _time)));
        Assert.Equal(text, File.ReadAllText(Record.FilePath));
    }
}
