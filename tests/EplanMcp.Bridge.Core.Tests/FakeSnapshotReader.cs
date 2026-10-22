using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests;

/// <summary>
/// A project of <see cref="PageCount"/> pages numbered 100, 110, …, each with one function and one connection, as the
/// snapshot adapter would read it. Every page read advances <see cref="Milliseconds"/> by <see cref="MillisecondsPerPage"/>.
/// </summary>
internal sealed class FakeSnapshotReader : ISnapshotReader, IProjectFiles
{
    public int PageCount { get; set; } = 5;

    public long MillisecondsPerPage { get; set; } = 1000;

    public long Milliseconds { get; private set; }

    public int Database { get; set; } = 28;

    public string Status { get; set; } = "IsCompatible";

    public List<string> Opened { get; } = [];

    public List<string> OpenedReadOnly { get; } = [];

    public int Disposed { get; private set; }

    public Dictionary<long, string> Descriptions { get; } = [];

    public ISnapshotSource Open(string projectPath)
    {
        Opened.Add(projectPath);
        return new Source(this, projectPath);
    }

    public ISnapshotSource OpenReadOnly(string linkFile)
    {
        OpenedReadOnly.Add(linkFile);
        return new Source(this, linkFile.Substring(0, linkFile.Length - 4));
    }

    public (string Status, string DatabaseVersion) Compatibility(string linkFile) => (Status, "18.0");

    public static string PageName(long oid) => "=AP+ST1/" + (oid / 10);

    private sealed class Source(FakeSnapshotReader owner, string path) : ISnapshotSource
    {
        public string Name => System.IO.Path.GetFileName(path);

        public string Path => path;

        public string? ProjectGuid => "Line1@1792643412";

        public string? ModifiedDate => "1792643412";

        public string? ModifiedTime => "36000";

        public IReadOnlyList<KeyValuePair<long, string>> Pages =>
            [.. Enumerable.Range(0, owner.PageCount).Reverse().Select(i => new KeyValuePair<long, string>(100 + (10 * i), PageName(100 + (10 * i))))];

        public int SkippedConnections { get; private set; }

        public IReadOnlyList<SnapshotSourceObject> ReadPage(long pageOid, string profile)
        {
            owner.Milliseconds += owner.MillisecondsPerPage;
            SkippedConnections++;
            string page = PageName(pageOid);
            string db = owner.Database.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return
            [
                new() { Kind = "page", Identifier = $"{db}/4/{pageOid}/0", Oid = pageOid, Name = page, PageType = "Circuit", Values = [new(11011, null, "de_DE@" + owner.Descriptions.GetValueOrDefault(pageOid, "Seite") + ";")] },
                new() { Kind = "function", Identifier = $"{db}/17/{pageOid + 1}/0", Oid = pageOid + 1, Name = "=+-K" + pageOid, Page = page, Category = "Coil", Main = true, X = 40, Y = 280 },
                new()
                {
                    Kind = "connection",
                    Identifier = $"{db}/30/{pageOid + 2}/0",
                    Oid = pageOid + 2,
                    Page = page,
                    Start = new SnapshotSourceEnd { Identifier = $"{db}/17/{pageOid + 1}/0", Oid = pageOid + 1, Pin = "A1" },
                },
            ];
        }

        public void Dispose() => owner.Disposed++;
    }
}
