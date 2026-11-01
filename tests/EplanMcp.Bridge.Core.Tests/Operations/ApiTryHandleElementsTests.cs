using System.Collections;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ApiTryHandleElementsTests
{
    private sealed class Source(int count) : IEnumerable
    {
        public bool Disposed { get; private set; }
        public int Advanced { get; private set; }
        public IEnumerator GetEnumerator() => Iterate().GetEnumerator();
        private IEnumerable<object> Iterate()
        {
            try
            { for (int i = 0; i < count; i++) { Advanced++; yield return new object(); } }
            finally { Disposed = true; }
        }
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(200)]
    [InlineData(201)]
    public void EachElementHasAnIndependentResolvableHandleAndEnumerationIsBounded(int count)
    {
        var table = new ApiTryHandleTable(() => DateTime.UnixEpoch);
        var source = new Source(count);
        ApiObjectHandle[] handles = ApiTryHandleElements.Map(source, v => table.Add("host", v, "T", "root.elements", () => "scratch"));
        Assert.Equal(Math.Min(200, count), handles.Length);
        Assert.Equal(handles.Length, handles.Select(h => h.Handle).Distinct(StringComparer.Ordinal).Count());
        Assert.All(handles, h => Assert.NotNull(table.Resolve("host", h, "T", "scratch", (_, _) => true, _ => true, out _).Value));
        Assert.Equal(handles.Length, source.Advanced);
        Assert.True(source.Disposed);
    }
    [Fact]
    public void NullElementsAreOmittedAndCaptureFailureDisposesEnumerator()
    {
        Assert.Single(ApiTryHandleElements.Map(new object?[] { null, new() }, _ => new ApiObjectHandle()));
        var source = new Source(10);
        Assert.Throws<BridgeErrorException>(() => ApiTryHandleElements.Map<ApiObjectHandle>(source, _ => throw new BridgeErrorException(ErrorReason.PolicyDenied, "foreign project")));
        Assert.True(source.Disposed);
        Assert.Equal(1, source.Advanced);
    }
}
