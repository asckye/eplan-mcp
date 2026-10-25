using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlanNamesOperationTests
{
    private const string Project = @"D:\Projects\Line1";
    private static readonly OpenProject _project = new() { Name = "Line1", Path = Project, ReadOnly = true };

    [Fact]
    public void Read_MergesFinderAndPlacements_DeduplicatesIdentityAndNamesAndSortsOrdinally()
    {
        var inventory = new PlanNameInventory { ProjectGuid = "Line1@123" };
        inventory.Pages.AddRange(["+B/1", "+A/1", "+A/1"]);
        // Finder: auxiliary, two same-name mains, unplaced, and unnamed.
        inventory.AddFunction(Function(1, "z", "+B/1"));
        inventory.AddFunction(Function(2, "A", "+A/1", main: true));
        inventory.AddFunction(Function(3, "A", null, main: true));
        inventory.AddFunction(Function(4, "", null));
        // Placements: repeated identity and additional representation absent from finder.
        inventory.AddFunction(Function(1, "z", "+B/1"));
        inventory.AddFunction(Function(5, "a", "+B/1"));
        inventory.AddFunction(Function(5, "aux", "+A/1", type: 18));
        inventory.AddFunction(Function(6, " ", null));
        var reader = new Reader(inventory);
        var operation = new PlanNamesOperation(new FakeCatalog(_project), reader, () => 0);

        PlanNamesData data = Assert.IsType<PlanNamesData>(operation.Execute(new PlanNamesArgs { Project = "d:/projects/line1.elk" }));

        Assert.Equal(7, inventory.Functions.Count);
        Assert.Equal([("function", " "), ("function", "A"), ("function", "a"), ("function", "aux"), ("function", "z"),
            ("page", "+A/1"), ("page", "+B/1")], data.Names.Select(n => (n.Kind, n.Name)));
        Assert.Equal(("Line1", Project, "Line1@123", true), (data.Project, data.ProjectPath, data.ProjectGuid, data.Complete));
        Assert.Equal(Project, reader.Path);
        Assert.Equal(1, reader.Calls);
        Assert.False(typeof(IWriteOperation).IsAssignableFrom(operation.GetType()));
        Assert.NotNull(data.NamesHash);
    }

    [Fact]
    public void Read_EmptyProject_IsCompleteWithAnEmptySetAndADigest()
    {
        PlanNamesData data = PlanNamesOperation.Build(_project, new(), () => { });
        Assert.True(data.Complete);
        Assert.Empty(data.Names);
        Assert.Null(data.ProjectGuid);
        Assert.Equal(64, data.NamesHash!.Length);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(1, false)]
    public void Read_DistinctNameLimit_IsInclusiveAndNeverReturnsAPartialSet(int limit, bool complete)
    {
        var inventory = new PlanNameInventory();
        inventory.Pages.AddRange(["same", "same"]);
        inventory.AddFunction(Function(1, "same", null));
        PlanNamesData data = PlanNamesOperation.Build(_project, inventory, () => { }, maxNames: limit);
        Assert.Equal(complete, data.Complete);
        Assert.Equal(complete ? 2 : 0, data.Names.Count);
        Assert.Equal(complete, data.NamesHash is not null);
        Assert.Equal(250_000, PlanNamesOperation.MaxNames);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    public void Read_JsonLimit_CountsUtf8EscapesArrayAndSeparators(int adjustment, bool complete)
    {
        var inventory = new PlanNameInventory();
        inventory.Pages.AddRange(["电机\"\\\n", "😀"]);
        PlanNamesData full = PlanNamesOperation.Build(_project, inventory, () => { });
        int bytes = BridgeJson.ToUtf8(BridgeJson.FromObject(full.Names)).Length;
        PlanNamesData data = PlanNamesOperation.Build(_project, inventory, () => { }, maxJsonBytes: bytes + adjustment);
        Assert.Equal(complete, data.Complete);
        Assert.Equal(complete ? 2 : 0, data.Names.Count);
        Assert.Equal(complete, data.NamesHash is not null);
        Assert.Equal(8 * 1024 * 1024, PlanNamesOperation.MaxJsonBytes);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"project":null}""")]
    [InlineData("""{"project":" "}""")]
    [InlineData("[]")]
    public void Read_InvalidArguments_NeverEnumerates(string json)
    {
        var reader = new Reader(new());
        var operation = new PlanNamesOperation(new FakeCatalog(_project), reader, () => 0);
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => operation.Execute(operation.ParseArgs(JToken.Parse(json))));
        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public void Read_ProjectNotOpen_NeverEnumerates()
    {
        var reader = new Reader(new());
        var operation = new PlanNamesOperation(new FakeCatalog(), reader, () => 0);
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => operation.Execute(new PlanNamesArgs { Project = "Line1" }));
        Assert.Equal(ErrorReason.ProjectNotOpen, error.Error.Reason);
        Assert.Equal(0, reader.Calls);
    }

    [Theory]
    [InlineData(9_999, true)]
    [InlineData(10_000, false)]
    public void Read_CheckpointChecksClockAfterABatch(long elapsed, bool complete)
    {
        long now = 0;
        var reader = new Reader(new()) { AtCheckpoint = () => now = elapsed };
        var operation = new PlanNamesOperation(new FakeCatalog(_project), reader, () => now);
        if (complete)
        {
            Assert.True(Assert.IsType<PlanNamesData>(operation.Execute(new PlanNamesArgs { Project = "Line1" })).Complete);
        }
        else
        {
            BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => operation.Execute(new PlanNamesArgs { Project = "Line1" }));
            Assert.Equal(ErrorReason.Timeout, error.Error.Reason);
            Assert.Null(error.Error.Execution);
        }
    }

    [Fact]
    public void Read_CheckpointObservesCancellation_WithoutAWriteOutcome()
    {
        var context = new OperationContext("cancel-names", write: false, progress: null);
        var reader = new Reader(new()) { AtCheckpoint = context.Cancel };
        var operation = new PlanNamesOperation(new FakeCatalog(_project), reader, () => 0);
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => context.Run(() => operation.Execute(new PlanNamesArgs { Project = "Line1" })));
        Assert.Equal(ErrorReason.Cancelled, error.Error.Reason);
        Assert.Null(error.Error.Execution);
    }

    private static PlanFunction Function(long oid, string name, string? page, bool main = false, int type = 17) =>
        new() { Oid = oid, Type = type, Name = name, Page = page, IsMain = main };

    private sealed class Reader(PlanNameInventory inventory) : IPlanNameReader
    {
        public string? Path { get; private set; }
        public int Calls { get; private set; }
        public Action? AtCheckpoint { get; init; }

        public IReadOnlyList<PlanFunction> Read(string projectPath) => throw new InvalidOperationException("A second enumeration is not allowed.");

        public PlanNameInventory ReadNames(string projectPath, Action checkpoint)
        {
            Path = projectPath;
            Calls++;
            AtCheckpoint?.Invoke();
            checkpoint();
            return inventory;
        }
    }
}
