using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>The call's messages use copied records; collection failures cannot undo a known committed outcome.</summary>
[Trait("Size", "Small")]
public sealed class ImportListMessageTests
{
    private readonly FakeCatalog _catalog = new(new OpenProject { Name = "Line1", Path = @"D:\Scratch\Line1" });
    private readonly FakePageReader _pages = new();
    private readonly FakeTransactions _transactions = new();
    private readonly Messages _messages = new();
    private readonly Importer _importer = new();
    private Action _onBackup = () => { };

    [Fact]
    public void Success_OnlyNewMessages_MapsLevelsAndLimitsToTwenty()
    {
        _messages.Records.Add(new() { Text = "old", Level = 4 });
        _importer.Run = () =>
        {
            _messages.Records.Add(new() { Text = "trace", Level = 0 });
            _messages.Records.AddRange(Enumerable.Range(0, 25).Select(i => new SystemMessageRecord { Text = "new", Index = i % 2 == 0 ? "S000000" : string.Empty, Level = i % 6 + 1, Occurrences = 2 }));
            return true;
        };

        ImportListData result = Execute();

        Assert.Equal(25, result.EplanMessageCount);
        Assert.Equal(20, result.EplanMessages.Count);
        Assert.All(result.EplanMessages, m => { Assert.Equal("new", m.Text); Assert.Null(m.Number); Assert.Equal(2, m.Occurrences); });
        Assert.Equal(["message", "warning", "warning", "error", "fatal", "message"], result.EplanMessages.Take(6).Select(m => m.Level));
        Assert.Equal(2, _messages.Reads);
    }

    [Fact]
    public void BackupMessages_AreNotTheCalls()
    {
        _onBackup = () => _messages.Records.Add(new() { Text = @"Started opening database: C:\Users\x\backups\Line1.edb", Level = 1 });
        _importer.Run = () => { _messages.Records.Add(new() { Text = "imported", Level = 2 }); return true; };

        ImportListData result = Execute();

        SystemMessage message = Assert.Single(result.EplanMessages);
        Assert.Equal((1, "imported"), (result.EplanMessageCount, message.Text));
    }

    [Fact]
    public void FailedAction_IncludesNewMessagesInRolledBackError()
    {
        _importer.Run = () => { _messages.Records.Add(new() { Text = "Import refused.", Index = "S123456", Level = 4 }); return false; };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute()).Error;
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        JObject message = (JObject)Assert.Single(JArray.Parse(error.Metadata!["eplanMessages"]));
        Assert.Equal(("S123456", "Import refused."), ((string?)message["number"], (string?)message["text"]));
        Assert.Contains("Rollback", _transactions.Calls);
    }

    [Fact]
    public void CollectionFailureAfterCommit_IsAWarning()
    {
        _messages.FailAfterBookmark = true;
        ImportListData result = Execute();
        Assert.Contains("Commit", _transactions.Calls);
        Assert.DoesNotContain("Rollback", _transactions.Calls);
        Assert.Contains("messages could not be read", result.Warning, StringComparison.Ordinal);
    }

    private ImportListData Execute()
    {
        var args = new ImportListArgs { Project = "Line1", Kind = "devices", File = "list.xml", FilePath = @"D:\Imports\list.xml", Sha256 = new string('a', 64) };
        args.Fingerprint = ((WritePlan)new ImportListPlanOperation(_catalog, _importer, Observe).Execute(args)!).Fingerprint;
        return (ImportListData)new ImportListExecuteOperation(_catalog, _pages, _importer, new FakeProjectBackup(_pages), _transactions,
            Observe, (_, _) => _onBackup(), () => () => TimeSpan.Zero, _messages).Execute(args)!;
    }

    private static void Observe(ImportSource source) => source.Observe(25, 1, new string('a', 64));

    private sealed class Importer : IListImporter
    {
        public Func<bool> Run { get; set; } = () => true;
        public ListImportState Read(string projectPath) => new(new ListImportCounts(), "state");
        public bool Import(string projectPath, string kind, string filePath, string converter) => Run();
    }

    private sealed class Messages : ISystemMessageReader
    {
        public List<SystemMessageRecord> Records { get; } = [];
        public int Reads { get; private set; }
        public bool FailAfterBookmark { get; set; }
        public IReadOnlyList<SystemMessageRecord> ReadAll() => ++Reads > 1 && FailAfterBookmark ? throw new InvalidOperationException("Messages unavailable.") : Records.ToArray();
    }
}
