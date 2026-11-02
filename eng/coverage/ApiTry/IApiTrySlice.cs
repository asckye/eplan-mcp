namespace EplanMcp.Coverage;

internal interface IApiTrySlice
{
    public string Name { get; }
    public ApiTryContribution Contribute(ApiTryGenerator.SliceInput input);
}

internal sealed record ApiTryAdapter(string Name, Func<ApiTryGenerator.SliceInput, ApiTryContribution> Factory) : IApiTrySlice
{
    public ApiTryContribution Contribute(ApiTryGenerator.SliceInput input) => Factory(input);
}
