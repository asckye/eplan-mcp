using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>shield.create.plan and shield.create.execute against fakes.</summary>
[Trait("Size", "Small")]
public sealed class ShieldCreateOperationTests
{
    private const string Page = "=F+A1&EFS1/1";

    private readonly FakeCatalog _catalog = new(new OpenProject { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true });
    private readonly FakePageReader _pages = new();
    private readonly Shields _shields = new();

    public ShieldCreateOperationTests() => _pages.Pages.Add(new PageSummary { Name = Page });

    [Fact]
    public void Plan_NamesTheShield_AndWritesNothing()
    {
        WritePlan plan = Plan(Args());
        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("create", "shield", "=F+A1-W1"), (change.Action, change.Kind, change.Name));
        Assert.Contains("90,148 - 130,152", change.NewValue, StringComparison.Ordinal);
        Assert.Empty(_shields.Created);
    }

    [Theory]
    [InlineData(90, 148, 90, 152)]
    [InlineData(90, 148, 130, 148)]
    [InlineData(-1, 148, 130, 152)]
    public void Plan_AnEmptyOrOutsideArea_IsInvalid(double x, double y, double cornerX, double cornerY)
    {
        ShieldCreateArgs args = Args();
        (args.X, args.Y, args.CornerX, args.CornerY) = (x, y, cornerX, cornerY);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
    }

    [Fact]
    public void Plan_AWirePointOutsideTheArea_IsInvalid()
    {
        ShieldCreateArgs args = Args();
        args.WirePoints = [[100, 160]];
        Assert.Contains("wire_points", Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_AnUnknownSymbolOrPage_IsRefused()
    {
        ShieldCreateArgs symbol = Args();
        symbol.Symbol = "XX";
        Assert.Contains("SH, SH2", Assert.Throws<BridgeErrorException>(() => Plan(symbol)).Error.Metadata!["violations"], StringComparison.Ordinal);
        ShieldCreateArgs page = Args();
        page.Page = "=F+A1&EFS1/9";
        Assert.Equal(ErrorReason.PageNotFound, Assert.Throws<BridgeErrorException>(() => Plan(page)).Error.Reason);
    }

    [Fact]
    public void Execute_CreatesThenGenerates_InOneTransaction_AndReadsBack()
    {
        ShieldCreateArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        ShieldCreateData data = Execute(args);
        Assert.Equal(["create", "generate"], _shields.Calls);
        Assert.Equal(1, _shields.Transactions.Calls.Count(c => c == "Commit"));
        Assert.Equal(("Line1", "=F+A1-W1", null), (data.Project, data.Name, data.Warning));
        Assert.Single(data.Connections);
    }

    [Fact]
    public void Execute_OverNoConnection_WarnsAfterTheCommit()
    {
        _shields.Covers = [];
        ShieldCreateArgs args = Args();
        args.WirePoints = null;
        args.Fingerprint = Plan(args).Fingerprint;
        Assert.Contains("covers no connection", Execute(args).Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChangedSinceThePlan_IsStale_AndDoesNotCreate()
    {
        ShieldCreateArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        _shields.Objects.Add("new");
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Reason);
        Assert.Empty(_shields.Created);
    }

    private static ShieldCreateArgs Args() => new() { Project = "Line1", Page = Page, X = 90, Y = 148, CornerX = 130, CornerY = 152, Name = "-W1", WirePoints = [[100, 150]] };

    private WritePlan Plan(ShieldCreateArgs args) => (WritePlan)new ShieldCreatePlanOperation(_catalog, _pages, _shields, _shields).Execute(args)!;

    private ShieldCreateData Execute(ShieldCreateArgs args) => (ShieldCreateData)new ShieldCreateExecuteOperation(_catalog, _pages, _shields, _shields).Execute(args)!;

    private sealed class Shields : IShieldCreator, IConnectionGenerator
    {
        public FakeTransactions Transactions { get; } = new();

        public List<string> Calls { get; } = [];

        public List<string> Created { get; } = [];

        public List<string> Objects { get; } = ["1"];

        public List<string> Covers { get; set; } = ["-X1:1 -> -X1:2"];

        public IReadOnlyList<string> Symbols(string projectPath) => ["SH", "SH2"];

        ITransactionScopeFactory IShieldCreator.Transactions(string projectPath) => Transactions;

        public ShieldObjects Create(string projectPath, string page, string symbol, string? name, ShieldCreateArgs area)
        {
            Calls.Add("create");
            Created.Add(name ?? string.Empty);
            return new ShieldObjects { Shield = 7, DefinitionPoints = [8] };
        }

        public ShieldCreateData Read(string projectPath, string page, ShieldObjects objects) => new() { Page = page, Name = Created.Single(), Symbol = "SH", Connections = Covers };

        public void Generate(string projectPath, IReadOnlyList<string> pages) => Calls.Add("generate");

        public IReadOnlyList<IReadOnlyList<ConnectionRecord>> ReadConnections(string projectPath, IReadOnlyList<string> pages) => [[]];

        public IReadOnlyList<IReadOnlyList<string>> ObjectIds(string projectPath, IReadOnlyList<string> pages) => [Objects];
    }
}
