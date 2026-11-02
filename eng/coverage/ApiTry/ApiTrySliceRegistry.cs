namespace EplanMcp.Coverage;

internal static class ApiTrySliceRegistry
{
    internal static IReadOnlyList<IApiTrySlice> Slices { get; } = Array.AsReadOnly<IApiTrySlice>([
        new ApiTryAdapter("Properties", ApiTryGenerator.PropertiesContribution),
        new ApiTryAdapter("Methods", ApiTryGenerator.MethodsContribution),
        new ApiTryAdapter("StaticEntries", ApiTryGenerator.EntriesContribution),
        new ApiTryAdapter("Files", ApiTryGenerator.FilesContribution),
        new ApiTryAdapter("Selection", _ => new()),
        new ApiTryAdapter("Settings", ApiTryGenerator.SettingsContribution),
        new ApiTryAdapter("Handles", ApiTryGenerator.HandlesContribution),
        new ApiTryAdapter("ArraysOut", ApiTryGenerator.ArraysContribution),
        new ApiTryAdapter("NativePreconditions", _ => new()),
        new ApiTryAdapter("Lifecycle", ApiTryGenerator.LifecycleContribution),
        new ApiTryAdapter("HandleChains", ApiTryGenerator.ChainsContribution),
    ]);
}
