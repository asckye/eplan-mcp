using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ApiTryTargetsTests
{
    [Theory]
    [InlineData("type")]
    [InlineData("oid")]
    [InlineData("page")]
    [InlineData("kind")]
    [InlineData("digest")]
    public void NearbyObjectStillRequiresEveryIdentityCheck(string changed)
    {
        var obj = new SnapshotObject { Kind = "function", Type = 17, Oid = 42, Page = "=A/1", Name = "-K1" };
        var locator = new DrawingLocator { Page = obj.Page, Type = obj.Type, Oid = obj.Oid, Digest = SnapshotCanonical.Sha256(JsonConvert.SerializeObject(obj)) };
        switch (changed)
        {
            case "type":
                obj.Type++;
                break;
            case "oid":
                obj.Oid++;
                break;
            case "page":
                obj.Page = "=A/2";
                break;
            case "kind":
                obj.Kind = "terminal";
                break;
            case "digest":
                obj.Name = "-K2";
                break;
        }
        int scans = 0;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => ApiTryTargets.Resolve(locator, ["function"], [obj],
            _ => { scans++; return Array.Empty<SnapshotObject>(); },
            (candidate, kind) => candidate.Kind == kind ? (candidate, candidate.Page!) : null)).Error;
        Assert.Equal(ErrorReason.PlanStale, error.Reason);
        Assert.Equal(1, scans);
    }

    [Fact]
    public void UnplacedObjectStillUsesCatalogDiscovery()
    {
        var obj = new SnapshotObject { Kind = "function", Type = 17, Oid = 42 };
        var locator = new DrawingLocator { Page = "", Type = obj.Type, Oid = obj.Oid, Digest = SnapshotCanonical.Sha256(JsonConvert.SerializeObject(obj)) };
        Assert.Same(obj, ApiTryTargets.Resolve(locator, ["function"], Array.Empty<SnapshotObject>(), _ => [obj],
            (candidate, _) => (candidate, "")));
    }

    [Fact]
    public void PageLocatorFromABaseKindKeepsItsRecordedPageAndOriginalDigest()
    {
        var obj = new SnapshotObject { Kind = "group", Type = 1, Oid = 42 };
        var locator = new DrawingLocator { Page = "=A/1", Type = obj.Type, Oid = obj.Oid, Digest = SnapshotCanonical.Sha256(JsonConvert.SerializeObject(obj)) };
        Assert.Same(obj, ApiTryTargets.Resolve(locator, ["group"], [obj], _ => throw new InvalidOperationException("No fallback needed."),
            (candidate, _) => (candidate, "=A/1")));
    }
}
