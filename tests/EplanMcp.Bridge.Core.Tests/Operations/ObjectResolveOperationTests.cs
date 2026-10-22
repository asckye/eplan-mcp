using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// object.resolve and its checks (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.6): each reason in its order, the
/// position tolerance, the hash of a resolved object equal to the snapshot's, and malformed locators refused whole.
/// </summary>
[Trait("Size", "Small")]
public sealed class ObjectResolveOperationTests
{
    private const string Project = @"C:\scratch\Line1";
    private readonly Finder _finder = new();
    private readonly ObjectResolveOperation _operation;

    public ObjectResolveOperationTests() =>
        _operation = new ObjectResolveOperation(new FakeCatalog(new OpenProject { Name = "Line1", Path = Project }), _finder);

    [Fact]
    public void Resolve_AnUnchangedFunction_IsResolved_WithTheSnapshotsHash()
    {
        ObjectResolveData data = Run(Locator());

        LocatorResult result = Assert.Single(data.Results);
        Assert.Equal((0, LocatorResult.Resolved, (string?)null), (result.Index, result.Status, result.Reason));
        Assert.Equal(SnapshotCanonical.ObjectOf(Finder.Motor(), SnapshotCanonical.Registered("standard")!).Hash, result.Hash);
        Assert.Equal(("Line1", Project), (data.Project, data.ProjectPath));
    }

    [Theory]
    [InlineData(@"C:\scratch\Line2", 59641, 17, null, null, null, null, "project")]
    [InlineData(Project, 59642, 17, null, null, null, null, "not_found")]
    [InlineData(Project, 59641, 18, "=+-QA9", null, null, null, "type")]
    [InlineData(Project, 59641, 17, "=+-QA9", "=AP+ST1/9", null, null, "name")]
    [InlineData(Project, 59641, 17, null, "=AP+ST1/9", "Coil", null, "page")]
    [InlineData(Project, 59641, 17, null, null, "Coil", "1,2", "category")]
    [InlineData(Project, 59641, 17, null, null, null, "120.002,180.5", "position")]
    public void Resolve_TheFirstFailedCheck_IsTheReason_AndNothingElseIsReturned(string project, long oid, int type, string? name, string? page, string? category, string? pos, string reason)
    {
        ObjectLocator locator = Locator();
        (locator.Project, locator.Oid, locator.Type) = (project, oid, type);
        (locator.Name, locator.Page, locator.Category, locator.Pos) = (name ?? locator.Name, page ?? locator.Page, category ?? locator.Category, pos ?? locator.Pos);

        LocatorResult result = Assert.Single(RunIn(Project, locator).Results);

        Assert.Equal((LocatorResult.Stale, reason, (string?)null), (result.Status, result.Reason, result.Hash));
    }

    [Fact]
    public void Resolve_AStaleOne_TellsWhatWasFound()
    {
        ObjectLocator moved = Locator();
        moved.Page = "=AP+ST1/9";

        LocatorResult result = Assert.Single(Run(moved).Results);

        Assert.Equal(("=+-CU1", "=AP+ST1/3", "Motor", "120,180.5"), (result.ActualName, result.ActualPage, result.ActualCategory, result.ActualPos));
    }

    [Theory]
    [InlineData("120.001,180.499")]
    [InlineData("119.999,180.501")]
    public void Resolve_WithinTheTolerance_IsResolved(string pos)
    {
        ObjectLocator locator = Locator();
        locator.Pos = pos;

        Assert.Equal(LocatorResult.Resolved, Assert.Single(Run(locator).Results).Status);
    }

    [Fact]
    public void Resolve_OnlyNumbersAndKind_ChecksNothingElse()
    {
        LocatorResult result = Assert.Single(Run(new ObjectLocator { Project = Project + ".elk", Kind = "function", Type = 17, Oid = 59641 }).Results);

        Assert.Equal(LocatorResult.Resolved, result.Status);
    }

    [Fact]
    public void Resolve_MalformedLocators_AreOneInvalidArgument_ListingEach()
    {
        var bad = new ObjectLocator { Project = Project, Kind = "symbol", Type = 0, Oid = 1, Pos = "1;2" };

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Run(Locator(), bad));

        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        Assert.Equal(
            ["locators[1].kind must be page, function or connection.", "locators[1].type and .oid must be the positive numbers the snapshot gave.", "locators[1].pos must be 'x,y' in millimetres, as the snapshot gave it."],
            error.Error.Metadata!["violations"].Split('\n'));
        Assert.Equal(0, _finder.Calls);
    }

    [Fact]
    public void Resolve_NoLocators_IsInvalidArgument() =>
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Run()).Error.Reason);

    private static ObjectLocator Locator() => new()
    {
        Project = Project,
        Kind = "function",
        Type = 17,
        Oid = 59641,
        Name = "=+-CU1",
        Page = "=AP+ST1/3",
        Category = "Motor",
        Pos = "120,180.5",
    };

    /// <summary>Without 'project': the first locator's project is resolved.</summary>
    private ObjectResolveData Run(params ObjectLocator[] locators) => RunIn(null, locators);

    private ObjectResolveData RunIn(string? project, params ObjectLocator[] locators) =>
        (ObjectResolveData)_operation.Execute(_operation.ParseArgs(JObject.FromObject(new ObjectResolveArgs { Project = project, Locators = [.. locators] })))!;

    /// <summary>One function, S28's =+-CU1, found by its number only, as the adapter finds it.</summary>
    private sealed class Finder : IObjectFinder
    {
        public int Calls { get; private set; }

        public static SnapshotSourceObject Motor() => new()
        {
            Kind = "function",
            Identifier = "89/17/59641/0",
            Oid = 59641,
            Name = "=+-CU1",
            Page = "=AP+ST1/3",
            Category = "Motor",
            Main = true,
            X = 120.0004,
            Y = 180.5,
            Values = [new SnapshotSourceValue(20011, null, "de_DE@Motor;")],
        };

        public IReadOnlyList<SnapshotSourceObject?> Find(string projectPath, IReadOnlyList<ObjectLocator> locators, string profile)
        {
            Calls++;
            return [.. locators.Select(l => l.Kind == "function" && l.Oid == 59641 ? Motor() : null)];
        }
    }
}
