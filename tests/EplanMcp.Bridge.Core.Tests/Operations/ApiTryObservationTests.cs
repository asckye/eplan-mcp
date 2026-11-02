using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ApiTryObservationTests
{
    private static ApiTryArgs Rotate(params string[] pages)
    {
        JObject Locator(string page) => new() { ["project"] = "Scratch", ["page"] = page, ["type"] = 1, ["oid"] = 2, ["digest"] = new string('a', 64) };
        return new()
        {
            Group = "O:Eplan.EplApi.HEServices.Edit.Rotate",
            Project = "Scratch",
            Parameters = new JObject
            {
                ["target"] = Locator(string.Empty),
                ["plcmnts"] = new JArray(pages.Select(Locator)),
                ["oPntStart"] = new JObject { ["x"] = 0, ["y"] = 0 },
                ["angle"] = 90,
            }.ToString(),
        };
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void RotateLineArray_ObservesOnlyAffectedPagesOnce(int copies)
    {
        // Live failure input: Project anchor, one Line, Rotate around (0, 0) by 90 degrees.
        string page = FakeSnapshotReader.PageName(100);
        ApiTryArgs args = Rotate(Enumerable.Repeat(page, copies).ToArray());
        string[] scope = ApiTryObservations.Pages(args)!;
        Assert.Equal([page], scope);
        var reader = new FakeSnapshotReader { PageCount = 321, MillisecondsPerPage = 1000 };
        using ISnapshotSource source = reader.Open(args.Project);
        var drawings = new List<string>();
        Dictionary<string, string> Capture(string geometry) => ApiTryObservations.Capture(source, scope, () => { }, name => { drawings.Add(name); return geometry; });
        Dictionary<string, string> before = Capture("line-before");
        Dictionary<string, string> preflight = Capture("line-before");
        Dictionary<string, string> after = Capture("line-rotated");
        Assert.Equal(before, preflight);
        Assert.Equal(["drawing:100", "snapshot:100"], before.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(before["snapshot:100"], after["snapshot:100"]);
        Assert.NotEqual(before["drawing:100"], after["drawing:100"]);
        Assert.Equal(new[] { page, page, page }, drawings);
        Assert.Equal(3000, reader.Milliseconds); // Three single-page reads, rather than 963 page descriptions.
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void EmptyOrUnplacedArrayKeepsTheProjectObservation(int count)
        => Assert.Null(ApiTryObservations.Pages(Rotate(Enumerable.Repeat(string.Empty, count).ToArray())));

    [Fact]
    public void ArrayHandleWithoutPageEvidenceKeepsTheProjectObservation()
    {
        ApiTryArgs args = Rotate("=A/1");
        JObject p = JObject.Parse(args.Parameters);
        p["plcmnts"] = new JArray(new JObject { ["handle"] = "h:" + new string('a', 32), ["type"] = "Eplan.EplApi.DataModel.Placement" });
        args.Parameters = p.ToString();
        Assert.Null(ApiTryObservations.Pages(args));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LifecycleUsesTheSameSnapshotAndDrawingPageScope(bool remove)
    {
        string page = FakeSnapshotReader.PageName(100);
        ApiTryArgs args = Rotate(page);
        JObject parameters = JObject.Parse(args.Parameters);
        JObject locator = (JObject)parameters["plcmnts"]![0]!;
        args.Group = remove ? "O:Eplan.EplApi.DataModel.Placement.Remove" : "O:Eplan.EplApi.DataModel.Graphics.Line.Create";
        args.Parameters = (remove ? new JObject { ["target"] = locator } : new JObject
        {
            ["page"] = locator,
            ["pntStart"] = new JObject { ["x"] = 4, ["y"] = 4 },
            ["pntEnd"] = new JObject { ["x"] = 12, ["y"] = 4 },
        }).ToString();
        string[] scope = ApiTryObservations.Pages(args)!;
        Assert.Equal([page], scope);
        var reader = new FakeSnapshotReader { PageCount = 321, MillisecondsPerPage = 1000 };
        using ISnapshotSource source = reader.Open(args.Project);
        var drawings = new List<string>();
        Dictionary<string, string> observed = ApiTryObservations.Capture(source, scope, () => { }, name => { drawings.Add(name); return "geometry"; });
        Assert.Equal([page], drawings);
        Assert.Equal(["drawing:100", "snapshot:100"], observed.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(1000, reader.Milliseconds);
    }

    [Fact]
    public void MultiplePagesAreDeduplicated_AndMissingEvidenceIsReported()
    {
        string page = FakeSnapshotReader.PageName(100);
        string[] scope = ApiTryObservations.Pages(Rotate(page, "missing", page))!;
        Assert.Equal(new[] { page, "missing" }, scope);
        using ISnapshotSource source = new FakeSnapshotReader().Open("Scratch");
        Dictionary<string, string> observed = ApiTryObservations.Capture(source, scope, () => { }, _ => null);
        Assert.Equal("unavailable", observed["observation:missing-page:missing"]);
        Assert.Contains("snapshot:100", observed.Keys);
        Assert.Equal("unavailable", observed["observation:drawing-unavailable:100"]);
        Assert.DoesNotContain("snapshot:110", observed.Keys);
    }
}
