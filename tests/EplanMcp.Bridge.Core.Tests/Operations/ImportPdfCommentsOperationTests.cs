using System.Security.Cryptography;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>A39/S44 semantics without EPLAN; intake uses a temporary file, never a committed PDF fixture.</summary>
[Trait("Size", "Medium")]
public sealed class ImportPdfCommentsOperationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-pdf-comments-").FullName;
    private readonly FakeCatalog _catalog = new(new OpenProject { Name = "Line1", Path = @"D:\Scratch\Line1.elk" });
    private readonly Importer _importer = new();
    private readonly FakeTransactions _transactions = new();
    private readonly Messages _messages = new();
    private readonly string _path;

    public ImportPdfCommentsOperationTests()
    {
        _path = Path.Combine(_root, "review.pdf");
        File.WriteAllBytes(_path, [1, 2, 3]);
        _transactions.Snapshot = () =>
        {
            string[] before = [.. _importer.Ids];
            return () => { _importer.Ids.Clear(); _importer.Ids.AddRange(before); };
        };
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Plan_ValidatesCountsAndNamesTheFile_WithoutPredictingAdded()
    {
        WritePlan plan = Plan(Args());

        Assert.Equal(("Line1", @"D:\Scratch\Line1.elk"), (plan.Project, plan.ProjectPath));
        Assert.Equal("1", plan.Details!["before"]);
        Assert.Equal("3", plan.Details["bytes"]);
        Assert.Equal(64, plan.Details["sha256"].Length);
        Assert.Contains("sticky notes only; number of new comments unknown", Assert.Single(plan.Changes).NewValue, StringComparison.Ordinal);
        Assert.Equal(0, _importer.Imports);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void Import_AndReimport_CommitExactlyOnce_ThenRollbackWithoutClosingAnEmptyStep()
    {
        ImportPdfCommentsArgs args = Planned();
        ImportPdfCommentsData first = Execute(args);
        args = Planned();
        ImportPdfCommentsData again = Execute(args);

        Assert.Equal((ExecutionState.Committed, 1, 2, 1), (first.Execution, first.Before, first.After, first.Added));
        Assert.Equal(("=GAA+A1&EFS1/1", "note", "??_??", "reviewer"), (first.Comments[0].Page, first.Comments[0].Text, first.Comments[0].Language, first.Comments[0].Author));
        Assert.Equal((ExecutionState.NotStarted, 2, 2, 0), (again.Execution, again.Before, again.After, again.Added));
        Assert.Empty(again.Comments);
        Assert.Equal(["Begin AI: eplan_import_pdf_comments", "Commit", "CloseUndo", "Dispose",
            "Begin AI: eplan_import_pdf_comments", "Rollback", "Dispose"], _transactions.Calls);
        Assert.Equal((@"D:\Scratch\Line1.elk", _path), _importer.LastImport);
    }

    [Fact]
    public void Rejection_AfterPartialChanges_IsInvalidArgument_RollsBackWithoutAnEmptyStep()
    {
        _importer.Reject = true;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(Planned())).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Contains("exported by EPLAN from this project", error.Message, StringComparison.Ordinal);
        Assert.Contains("Acrobat/Reader", error.Message, StringComparison.Ordinal);
        Assert.Equal(["old"], _importer.Ids);
        Assert.Equal(["Begin AI: eplan_import_pdf_comments", "Rollback", "Dispose"], _transactions.Calls);
    }

    [Theory]
    [InlineData("read")]
    [InlineData("cancel")]
    public void ReadBackFailureOrCancellation_RestoresExistingComments(string failure)
    {
        _importer.Failure = failure;
        var context = new OperationContext("r1", write: true, progress: null);

        BridgeError error = Assert.Throws<BridgeErrorException>(() => context.Run(() => Execute(Planned()))).Error;

        Assert.Equal(ExecutionState.RolledBack, error.Execution);
        Assert.Equal(failure == "read" ? ErrorReason.EplanOperationFailed : ErrorReason.Cancelled, error.Reason);
        Assert.Equal(["old"], _importer.Ids);
        Assert.DoesNotContain("Commit", _transactions.Calls);
        Assert.DoesNotContain("CloseUndo", _transactions.Calls);
    }

    [Fact]
    public void NoOp_WhoseRollbackFails_IsUnknown()
    {
        _importer.Ids.Add("new");
        _transactions.FailAt = "Rollback";

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(Planned())).Error;

        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Reason, error.Execution));
    }

    [Theory]
    [InlineData("content")]
    [InlineData("time")]
    [InlineData("comments")]
    public void FileOrProjectChangedAfterPlanning_IsStaleBeforeAnyTransaction(string change)
    {
        ImportPdfCommentsArgs args = Planned();
        if (change == "content")
        { File.WriteAllBytes(_path, [3, 2, 1]); }
        else if (change == "time")
        { File.SetLastWriteTimeUtc(_path, File.GetLastWriteTimeUtc(_path).AddMinutes(1)); }
        else
        { _importer.Ids.Add("another"); }

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_transactions.Calls);
        Assert.Equal(0, _importer.Imports);
    }

    [Fact]
    public void MissingFile_IsNotFound_WithoutLocalPath()
    {
        ImportPdfCommentsArgs args = Planned();
        File.Delete(_path);

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.FileNotFound, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.DoesNotContain(_root, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingProjectAndInvalidFile_ReportAllProblems()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(new ImportPdfCommentsArgs())).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Equal(4, error.Metadata!["violations"].Split('\n').Length);
    }

    [Fact]
    public void MissingFingerprint_IsNotStarted()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(Args())).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void LargeImport_ReturnsExactCounts_ButOnly200NewComments()
    {
        _importer.NewCount = 250;

        ImportPdfCommentsData data = Execute(Planned());

        Assert.Equal((1, 251, 250, 200), (data.Before, data.After, data.Added, data.Comments.Count));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyThisImportsMessages_AreCollectedOnCommitAndRejection(bool rejected)
    {
        _messages.Records.Add(new() { Text = "old", Level = 4 });
        _importer.Reject = rejected;
        _importer.AfterImport = () => _messages.Records.AddRange(Enumerable.Range(0, 25).Select(_ => new SystemMessageRecord { Text = "PDF rejected or imported", Level = 2, Index = "S123456", Occurrences = 1 }));
        if (rejected)
        {
            BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(Planned())).Error;
            Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.RolledBack), (error.Reason, error.Execution));
            Assert.Equal("25", error.Metadata!["eplanMessageCount"]);
            Assert.Equal(20, Newtonsoft.Json.Linq.JArray.Parse(error.Metadata["eplanMessages"]).Count);
            Assert.DoesNotContain("old", error.Metadata["eplanMessages"], StringComparison.Ordinal);
        }
        else
        {
            ImportPdfCommentsData data = Execute(Planned());
            Assert.Equal(25, data.EplanMessageCount);
            Assert.Equal(20, data.EplanMessages.Count);
            Assert.All(data.EplanMessages, m => Assert.Equal("PDF rejected or imported", m.Text));
        }
        Assert.Equal(2, _messages.Reads);
    }

    [Fact]
    public void MessageCollectionFailure_DoesNotReplaceKnownCommit()
    {
        _messages.FailAfterBookmark = true;
        ImportPdfCommentsData data = Execute(Planned());
        Assert.Equal(ExecutionState.Committed, data.Execution);
        Assert.Contains("messages could not be read", data.Warning, StringComparison.Ordinal);
    }

    private ImportPdfCommentsArgs Args() => new() { Project = "Line1", File = "review.pdf", FilePath = _path, Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(_path))) };

    private ImportPdfCommentsArgs Planned()
    {
        ImportPdfCommentsArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        return args;
    }

    private WritePlan Plan(ImportPdfCommentsArgs args) => (WritePlan)new ImportPdfCommentsPlanOperation(_catalog, _importer).Execute(args)!;

    private ImportPdfCommentsData Execute(ImportPdfCommentsArgs args) =>
        (ImportPdfCommentsData)new ImportPdfCommentsExecuteOperation(_catalog, _importer, _transactions, _messages).Execute(args)!;

    private sealed class Messages : ISystemMessageReader
    {
        public List<SystemMessageRecord> Records { get; } = [];
        public int Reads { get; private set; }
        public bool FailAfterBookmark { get; set; }
        public IReadOnlyList<SystemMessageRecord> ReadAll() => ++Reads > 1 && FailAfterBookmark ? throw new InvalidOperationException("Messages unavailable.") : [.. Records];
    }

    private sealed class Importer : IPdfCommentsImporter
    {
        public List<string> Ids { get; } = ["old"];
        public bool Reject { get; set; }
        public string? Failure { get; set; }
        public int NewCount { get; set; } = 1;
        public int Imports { get; private set; }
        public Action AfterImport { get; set; } = () => { };
        public (string Project, string File) LastImport { get; private set; }

        public IReadOnlyList<string> Identifiers(string projectPath) => [.. Ids];

        public bool Import(string projectPath, string filePath)
        {
            Imports++;
            LastImport = (projectPath, filePath);
            for (int i = 0; i < NewCount; i++)
            {
                string id = i == 0 ? "new" : i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!Ids.Contains(id))
                { Ids.Add(id); }
            }
            if (Failure == "cancel")
            { OperationContext.Current.Cancel(); }
            AfterImport();
            return !Reject;
        }

        public List<PdfComment> Read(string projectPath, IReadOnlyList<string> identifiers) => Failure == "read"
            ? throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Read-back failed.")
            : [.. identifiers.Select(_ => new PdfComment { Page = "=GAA+A1&EFS1/1", Text = "note", Author = "reviewer", Created = "2026-10-27T10:00:00", Modified = "2026-10-27T11:00:00", Position = "RX: 0.00 RY: 73.00" })];
    }
}
