using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests;

internal sealed class PlacementTextStore : IPlacementTextWriter
{
    public List<PlacementText> Texts { get; } = [];
    public FakeTransactions Transactions { get; } = new();
    public BridgeErrorException? ValidationFailure { get; set; }
    public Exception? WriteFailure { get; set; }
    public Func<PlacementText, PlacementText> Readback { get; set; } = t => t;
    public Action? AfterWrite { get; set; }
    public int Validations { get; private set; }
    public int Writes { get; private set; }
    public OpenProject Project { get; } = new() { Name = "Line1", Path = "project" };
    public PlacementTextCreateExecuteOperation Operation => new(new FakeCatalog(Project), this, Transactions);

    public PlacementTextStore()
    {
        Transactions.Snapshot = () =>
        {
            PlacementText[] before = [.. Texts.Select(Clone)];
            return () => { Texts.Clear(); Texts.AddRange(before); };
        };
    }

    public void Validate(string projectPath, string page, PlacementText text)
    {
        Validations++;
        Assert.Equal(("project", "P"), (projectPath, page));
        if (ValidationFailure is { } error)
        { throw error; }
    }

    public PlacementText Create(string projectPath, string page, PlacementText text)
    {
        Assert.Equal(("project", "P"), (projectPath, page));
        Assert.Equal("Begin AI: eplan_placement_create", Assert.Single(Transactions.Calls));
        Writes++;
        Texts.Add(Clone(text));
        if (WriteFailure is { } error)
        { throw error; }
        AfterWrite?.Invoke();
        return Readback(Clone(text));
    }

    public static PlacementTextCreateArgs Args() => new()
    {
        Project = "Line1",
        Page = "P",
        Text = new() { Position = [12.5, 22.5], HeightMm = 3.5, Contents = new(StringComparer.Ordinal) { ["en_US"] = "Panel", ["de_DE"] = "Feld" } },
    };

    public static PlacementText Clone(PlacementText text) => BridgeJson.FromObject(text).ToObject<PlacementText>(BridgeJson.Serializer)!;
}
