using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

internal static class ApiTryLifecycleObservations
{
    internal static void Committed(IApiTryDispatcher dispatcher, ApiTryArgs args, ApiTryData data)
    {
        if (ApiTryLifecycle.Effect(args.Group) is null)
        { return; }
        data.UndoExpected = true;
        data.After = ApiTryFiles.Observe(args, dispatcher.Inspect(args));
        if (data.Locator is { } locator)
        { data.Locator = ((IApiTryLifecycleDispatcher)dispatcher).Refresh(args.Project, locator); }
    }
}
