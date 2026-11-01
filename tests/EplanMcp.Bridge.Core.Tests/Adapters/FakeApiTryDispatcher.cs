using EplanMcp.Bridge.Core;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace Eplan.EplApi.DataModel
{
    internal sealed class StorableObject(DrawingLocator identity) : IDisposable
    {
        public DrawingLocator Identity { get; } = identity;
        public void Dispose() => throw new InvalidOperationException("Borrowed object must never be disposed.");
    }
}
namespace EplanMcp.Bridge.Eplan.Adapters
{
    internal sealed partial class EplanApiTryDispatcher
    {
        internal static object? Copy(object? value, Func<object, object>? native = null, Func<object, bool>? nativeType = null) => Detach("Scratch", value, native: native, nativeType: nativeType);
        private static DrawingLocator Locator(string project, global::Eplan.EplApi.DataModel.StorableObject obj) => obj.Identity.Project == project ? obj.Identity : throw new BridgeErrorException(ErrorReason.PolicyDenied, "Foreign project.");
        private static global::Eplan.EplApi.DataModel.StorableObject Resolve(string project, JToken token, string[] kinds) => throw new NotSupportedException();
    }
}
