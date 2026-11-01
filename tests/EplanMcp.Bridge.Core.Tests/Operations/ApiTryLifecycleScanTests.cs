using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ApiTryLifecycleScanTests
{
    private const string Project = @"D:\scratch\LargeWrite";
    private const string Page = "=A/901";
    private static readonly JsonSerializerSettings _json = new() { ContractResolver = new CamelCasePropertyNamesContractResolver() };
    private static DrawingLocator Locator(string page, long oid) => new() { Project = Project, Page = page, Type = 1, Oid = oid, Digest = new string('a', 64) };
    private static ApiTryArgs Factory() => new()
    {
        Project = Project,
        Group = "O:Eplan.EplApi.DataModel.Graphics.Line.#ctor+Create",
        Parameters = JsonConvert.SerializeObject(new
        {
            target = Locator(string.Empty, 1),
            page = Locator(Page, 2),
            pntStart = new { x = 4, y = 4 },
            pntEnd = new { x = 12, y = 4 },
        }, _json),
    };

    [Fact]
    public void LargeWriteLineFactoryAndRemovalObserveOnlySentinelPageWithinOriginalBudget()
    {
        ApiTryArgs args = Factory();
        int wholeProjectReads = 0, pageReads = 0, descriptions = 0;
        bool lineExists = false;
        TimeSpan elapsed = TimeSpan.Zero;
        IEnumerable<string> Source(string[]? pages, Action check)
        {
            if (pages is null)
            {
                wholeProjectReads++;
                // A32: an ordinary sample copy has 321 pages and 44,735 placements.
                for (int i = 0; i < 44735; i++)
                { elapsed += TimeSpan.FromMilliseconds(1); check(); yield return "unrelated:" + i; }
                yield break;
            }
            Assert.Equal([Page], pages);
            pageReads++;
            yield return "object:page:2";
            if (lineExists)
            { yield return "object:line:3"; }
        }
        Dictionary<string, string> Observe()
        {
            elapsed = TimeSpan.Zero;
            var scan = new ApiTryLifecycleScan(args, () => elapsed);
            Dictionary<string, string> state = scan.Read(Source, key => key, key => { descriptions++; return key; });
            Assert.Equal("pages:1", scan.Measurements()["scope"]);
            return state;
        }

        Dictionary<string, string> before = Observe();
        lineExists = true;
        Dictionary<string, string> created = Observe();
        Assert.DoesNotContain("object:line:3", before.Keys);
        Assert.Contains("object:line:3", created.Keys);
        args.Group = "O:Eplan.EplApi.DataModel.Placement.Remove";
        args.Parameters = JsonConvert.SerializeObject(new { target = Locator(Page, 3) }, _json);
        Assert.Equal(created, Observe());
        lineExists = false;
        Assert.Equal(before, Observe());
        Assert.Equal(0, wholeProjectReads);
        Assert.Equal(4, pageReads);
        Assert.Equal(6, descriptions);
    }

    [Fact]
    public void DuplicateCandidatesAreDescribedAndChargedOnlyOnce_ResourcesRemainInEvidence()
    {
        ApiTryArgs args = Factory();
        var scan = new ApiTryLifecycleScan(args, () => TimeSpan.Zero);
        int described = 0;
        string resource = new('x', 150000);
        Dictionary<string, string> state = scan.Read((pages, check) => new[] { Locator(Page, 2), Locator(Page, 2) },
            locator => "object:placeholder:" + locator.Oid,
            locator => { described++; Assert.True(scan.Touched(locator)); return resource; });
        Assert.Equal(resource, Assert.Single(state).Value);
        Assert.Equal(1, described);
        Assert.Equal("2", scan.Measurements()["visited"]);
        Assert.Equal("1", scan.Measurements()["objects"]);
        Assert.Equal("150000", scan.Measurements()["bytes"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(Page)]
    public void RemovalUsesPageWhenAvailable_AndUnpagedTargetsFallBackToOneProjectPass(string page)
    {
        var args = new ApiTryArgs { Group = "O:Eplan.EplApi.DataModel.Placement.Remove", Parameters = JsonConvert.SerializeObject(new { target = Locator(page, 3) }, _json) };
        Assert.Equal(page.Length == 0 ? null : new[] { Page }, ApiTryObservations.Pages(args));
    }

    [Theory]
    [InlineData("time")]
    [InlineData("bytes")]
    [InlineData("objects")]
    public void RealLimitsStillRefuseAndReportMeasuredWork(string limit)
    {
        var scan = new ApiTryLifecycleScan(Factory(), () => limit == "time" ? TimeSpan.FromSeconds(21) : TimeSpan.Zero);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => scan.Read((pages, check) => Enumerable.Range(0, 10001),
            i => "object:line:" + i, _ => limit == "bytes" ? new string('中', 87382) : "x")).Error;
        Assert.Equal(ErrorReason.Timeout, error.Reason);
        Assert.Equal("pages:1", error.Metadata!["scope"]);
        Assert.Equal(limit == "time" ? "21000" : "0", error.Metadata["elapsedMs"]);
        Assert.Equal(limit == "objects" ? "10001" : limit == "bytes" ? "1" : "0", error.Metadata["objects"]);
        Assert.Equal(limit == "bytes" ? "262146" : limit == "objects" ? "10001" : "0", error.Metadata["bytes"]);
    }
}
