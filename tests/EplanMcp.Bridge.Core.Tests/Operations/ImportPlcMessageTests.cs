using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>Only copied records are retained; a collection failure never changes the transaction outcome (A11).</summary>
[Trait("Size", "Small")]
public sealed class ImportPlcMessageTests
{
    private readonly FakeCatalog _catalog = new(new OpenProject { Name = "Line1", Path = @"D:\Scratch\Line1" });
    private readonly FakePageReader _pages = new();
    private readonly FakeTransactions _transactions = new();
    private readonly Messages _messages = new();
    private readonly Importer _importer = new();
    private Action _onBackup = () => { };

    public ImportPlcMessageTests()
    {
        _transactions.Snapshot = () => { PlcImportFunction[] before = _importer.Items; return () => _importer.Items = before; };
    }

    [Fact]
    public void Success_OnlyNewMessages_MapsLevelsAndCapsAtTwenty()
    {
        _messages.Records.Add(new() { Text = "old", Level = 4 });
        _importer.Run = () =>
        {
            _messages.Records.Add(new() { Text = "trace", Level = 0 });
            _messages.Records.AddRange(Enumerable.Range(0, 25).Select(i => new SystemMessageRecord { Text = "new", Index = i % 2 == 0 ? "S000000" : string.Empty, Level = i % 6 + 1, Occurrences = 2 }));
            return true;
        };

        ImportPlcData result = Execute();

        Assert.Equal((25, 20, 2), (result.EplanMessageCount, result.EplanMessages.Count, _messages.Reads));
        Assert.All(result.EplanMessages, m => { Assert.Equal("new", m.Text); Assert.Null(m.Number); Assert.Equal(2, m.Occurrences); });
        Assert.Equal(["message", "warning", "warning", "error", "fatal", "message"], result.EplanMessages.Take(6).Select(m => m.Level));
    }

    [Fact]
    public void BackupMessages_AreNotTheCalls()
    {
        _onBackup = () => _messages.Records.AddRange(Enumerable.Range(0, 40).Select(_ => new SystemMessageRecord { Text = @"Started opening database: C:\Users\x\backups\Line1.edb", Level = 1 }));
        _importer.Run = () => { _messages.Records.Add(new() { Text = "imported", Level = 2 }); return true; };

        ImportPlcData result = Execute();

        SystemMessage message = Assert.Single(result.EplanMessages);
        Assert.Equal((1, "imported"), (result.EplanMessageCount, message.Text));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActionFailureOrDuplicate_IncludesTheCallsMessagesInTheRolledBackError(bool duplicate)
    {
        _importer.Run = () =>
        {
            _messages.Records.Add(new() { Text = "Import warning.", Index = "S123456", Level = 4 });
            if (duplicate)
            { _importer.Items = [.. _importer.Items, new(2, "=F+A2-KE1", "PLCBox", true)]; }
            return duplicate;
        };

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute()).Error;

        Assert.Equal((duplicate ? ErrorReason.AlreadyExists : ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        JObject message = (JObject)Assert.Single(JArray.Parse(error.Metadata!["eplanMessages"]));
        Assert.Equal(("S123456", "Import warning."), ((string?)message["number"], (string?)message["text"]));
        Assert.Single(_importer.Items);
        Assert.Contains("Rollback", _transactions.Calls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void MessageReadFailure_IsOnlyAWarning_AfterACommit(int failAt)
    {
        _messages.FailAt = failAt;
        ImportPlcData result = Execute();
        Assert.Contains("Commit", _transactions.Calls);
        Assert.DoesNotContain("Rollback", _transactions.Calls);
        Assert.Contains("messages could not be read", result.Warning, StringComparison.Ordinal);
        Assert.Empty(result.EplanMessages);
    }

    [Fact]
    public void MessageReadFailure_DoesNotReplaceKnownRollback()
    {
        _messages.FailAt = 2;
        _importer.Run = () => false;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute()).Error;
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Contains("messages could not be read", error.Metadata!["messagesWarning"], StringComparison.Ordinal);
    }

    private ImportPlcData Execute()
    {
        var args = new ImportPlcArgs { Project = "Line1", File = "plc.pbf", FilePath = @"D:\Imports\plc.pbf", Sha256 = new string('a', 64), Converter = "PlcDcXMLExchangerUniversal" };
        args.Fingerprint = ((WritePlan)new ImportPlcPlanOperation(_catalog, _importer, Read).Execute(args)!).Fingerprint;
        return (ImportPlcData)new ImportPlcExecuteOperation(_catalog, _pages, _importer, new FakeProjectBackup(_pages), _transactions, Read, (_, _) => _onBackup(), _messages).Execute(args)!;
    }

    private static void Read(ImportSource source) => source.Observe(25, 1, new string('a', 64));

    private sealed class Importer : IPlcImporter
    {
        public PlcImportFunction[] Items { get; set; } = [new(1, "=F+A2-KE1", "PLCBox", true)];
        public Func<bool> Run { get; set; } = () => true;
        public PlcImportState Read(string projectPath) => new(Items, new() { Functions = Items.Length, PlcBoxes = Items.Length, ArticleReferences = Items.Length });
        public bool Import(string projectPath, string filePath, string converter) => Run();
    }

    private sealed class Messages : ISystemMessageReader
    {
        public List<SystemMessageRecord> Records { get; } = [];
        public int Reads { get; private set; }
        public int FailAt { get; set; } = -1;
        public IReadOnlyList<SystemMessageRecord> ReadAll() => ++Reads == FailAt ? throw new InvalidOperationException("Messages unavailable.") : Records.ToArray();
    }
}
