using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Tests.Adapters;

[Trait("Size", "Small")]
public sealed class ApiTryArrayAdapterTests
{
    [Fact]
    public void ReturnCollectionsBecomeReusableLocatorsAndTypedSimpleValues()
    {
        DrawingLocator identity = new() { Project = "Scratch", Page = "A", Type = 1, Oid = 2, Digest = new string('a', 64) };
        JArray result = Assert.IsType<JArray>(EplanApiTryDispatcher.Copy(new[] { new StorableObject(identity) }));
        Assert.Equal(identity.Digest, result[0]["digest"]!.Value<string>());
        Assert.Equal("Scratch", result[0]["project"]!.Value<string>());
        JArray values = Assert.IsType<JArray>(EplanApiTryDispatcher.Copy(new object[] { 1, true, "text", new PointD(2, 3) }));
        Assert.Equal(JTokenType.Integer, values[0].Type);
        Assert.Equal(JTokenType.Boolean, values[1].Type);
        Assert.Equal(2, values[3]["x"]!.Value<int>());
    }
    [Fact]
    public void NamedOutAndRefResultsAreDetachedWhileBorrowedObjectsAreAlive()
    {
        var named = new Dictionary<string, object?> { ["returnValue"] = true, ["outputs"] = new Dictionary<string, object?> { ["points"] = new[] { new PointD(1, 2), new PointD(3, 4) }, ["count"] = 2 } };
        JObject copied = Assert.IsType<JObject>(EplanApiTryDispatcher.Copy(named));
        Assert.True(copied["returnValue"]!.Value<bool>());
        Assert.Equal(2, Assert.IsType<JArray>(copied["outputs"]!["points"]).Count);
        Assert.Equal(2, copied["outputs"]!["count"]!.Value<int>());
        Assert.Null(EplanApiTryDispatcher.Copy(null));
        Assert.Equal("42", EplanApiTryDispatcher.Copy(42));
    }
    [Fact]
    public void OutputLimitIsBoundedAndNonfiniteOrForeignOutputsAreRejected()
    {
        Assert.Equal(200, Assert.IsType<JArray>(EplanApiTryDispatcher.Copy(Enumerable.Range(0, 200))).Count);
        Assert.Throws<InvalidOperationException>(() => EplanApiTryDispatcher.Copy(Enumerable.Range(0, 201)));
        Assert.Throws<InvalidOperationException>(() => EplanApiTryDispatcher.Copy(new PointD(double.NaN, 1)));
        Assert.Throws<InvalidOperationException>(() => EplanApiTryDispatcher.Copy(new[] { double.PositiveInfinity }));
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeErrorException>(() => EplanApiTryDispatcher.Copy(new[] { new StorableObject(new() { Project = "Real" }) })).Error.Reason);
    }
    [Fact]
    public void NestedNativeValuesUseTheSameConverterAndKeepOpaqueHandleWireFields()
    {
        var located = new StorableObject(new() { Project = "Scratch", Oid = 1 });
        var unlocated = new StorableObject(new() { Project = "Scratch" });
        object helper = new();
        var visited = new List<object>();
        object ConvertNative(object value)
        {
            visited.Add(value);
            return ReferenceEquals(value, located) ? located.Identity : new ApiObjectHandle { Handle = "h:" + new string('a', 32), Type = "Eplan.EplApi.DataModel.Pin" };
        }
        var named = new Dictionary<string, object?> { ["returnValue"] = new object[] { located, unlocated, helper, new PointD(2, 3) } };
        JObject result = Assert.IsType<JObject>(EplanApiTryDispatcher.Copy(named, ConvertNative));
        JArray items = Assert.IsType<JArray>(result["returnValue"]);
        Assert.Equal(1, items[0]["oid"]!.Value<int>());
        Assert.All(items.Skip(1).Take(2), token =>
        {
            Assert.Equal(["handle", "type"], Assert.IsType<JObject>(token).Properties().Select(p => p.Name));
            Assert.Equal("h:" + new string('a', 32), token["handle"]!.Value<string>());
        });
        Assert.Equal(2, items[3]["x"]!.Value<int>());
        Assert.Equal(new[] { located, unlocated, helper }, visited);
    }

    private sealed class BorrowedSequence : System.Collections.IEnumerable
    {
        public System.Collections.IEnumerator GetEnumerator() => throw new InvalidOperationException("Native objects must be handled before enumeration.");
    }
    [Fact]
    public void CompiledNativeObjectsHaveHandlePriorityEvenWhenTheyImplementEnumerable()
    {
        var native = new BorrowedSequence();
        ApiObjectHandle handle = new() { Handle = "h:" + new string('a', 32), Type = "Eplan.EplApi.DataModel.Pin" };
        JArray result = Assert.IsType<JArray>(EplanApiTryDispatcher.Copy(new object[] { native }, _ => handle, value => ReferenceEquals(value, native)));
        Assert.Equal(handle.Handle, result[0]["handle"]!.Value<string>());
        Assert.Equal(handle.Type, result[0]["type"]!.Value<string>());
    }
}
