using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;

#pragma warning disable CA1822 // Fake signatures match the native instance methods.

namespace Eplan.EplApi.HEServices
{
    internal sealed class DeviceService : IDisposable
    {
        [ThreadStatic]
        public static Func<string, string, Page, PointD, Function[]>? Create;
        public Function[] CreateDevice(string part, string variant, Page page, PointD point)
        {
            Function[] functions = Create!(part, variant, page, point);
            // A85: only newly created, unplaced device functions change from temporary to persistent IDs on commit.
            Transaction.Current?.TrackUnplacedDeviceFunctions(functions);
            return functions;
        }
        public void Dispose() { }
    }

    internal sealed class NameService : IDisposable
    {
        [ThreadStatic]
        public static Action<Function, FunctionBasePropertyList, bool, bool>? Rename;
        [ThreadStatic]
        public static Func<Page, FunctionBase, bool>? Adjust;
        public bool AdjustVisibleName(Page page, FunctionBase function)
        {
            if (Adjust is { } adjust)
            {
                return adjust(page, function);
            }

            if (function is Eplan.EplApi.DataModel.EObjects.Cable cable)
            {
                cable.AdjustVisibleName(page);
            }
            return false;
        }
        public void RenameDevice(Function main, FunctionBasePropertyList name, bool cdps, bool keep) => Rename!(main, name, cdps, keep);
        public void Dispose() { }
    }
}

namespace EplanMcp.Bridge.Eplan.Adapters
{
    internal static class EplanConnectionGenerator
    {
        public static string ForModel(string message) => message;
    }
}
