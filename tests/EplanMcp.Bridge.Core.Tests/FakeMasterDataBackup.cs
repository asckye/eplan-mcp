using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests;

/// <summary>
/// EPLAN's master-data backup as S24 saw it: one 7z archive named after <c>ARCHIVENAME</c> with <c>.zw6</c> in the
/// destination folder. <see cref="Write"/> changes what lands there; <see cref="Failure"/> makes the action fail; a
/// slow backup takes no real time: <see cref="StartClock"/> is the stopwatch it is timed with, which moves by
/// <see cref="Delay"/> for each backup.
/// </summary>
internal sealed class FakeMasterDataBackup : IMasterDataBackup
{
    public static readonly byte[] SevenZip = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0x00, 0x04];

    private TimeSpan _taken;

    public string Database { get; set; } = @"$(MD_PARTS)\EPLAN_parts.alk";

    public List<(string Database, string Folder, string ArchiveName)> Backups { get; } = [];

    public Exception? Failure { get; set; }

    public TimeSpan Delay { get; set; }

    /// <summary>What the backup leaves in the folder, given the folder and the archive name.</summary>
    public Action<string, string> Write { get; set; } = (folder, name) => File.WriteAllBytes(Path.Combine(folder, name + ".zw6"), SevenZip);

    public string PartsDatabase() => Database;

    public string TranslationDatabase() => Database;

    public void BackupTranslations(string database, string folder, string archiveName) => BackupParts(database, folder, archiveName);

    /// <summary>A stopwatch that has just started and reads how long the backups since then have taken.</summary>
    public Func<TimeSpan> StartClock()
    {
        TimeSpan from = _taken;
        return () => _taken - from;
    }

    public void BackupParts(string database, string folder, string archiveName)
    {
        Backups.Add((database, folder, archiveName));
        _taken += Delay;
        if (Failure is not null)
        {
            throw Failure;
        }

        Write(folder, archiveName);
    }
}
