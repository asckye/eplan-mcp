namespace EplanMcp.Coverage;

internal sealed class ApiTryComposer
{
    private readonly List<ApiTryContribution> _contributions = [];

    internal ApiTryComposer(string root, IEnumerable<IApiTrySlice> slices)
    {
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        var input = new ApiTryGenerator.SliceInput(root);
        foreach (IApiTrySlice slice in slices)
        {
            ApiTryContribution contribution = slice.Contribute(input);
            foreach (ApiTrySelection member in contribution.Members.Where(m => m.Claim == ApiTryClaim.Own && m.ExclusionReason is null))
            {
                if (!owners.TryAdd(member.Signature, slice.Name))
                {
                    throw new InvalidOperationException($"api_try signature '{member.Signature}' is owned by both slices '{owners[member.Signature]}' and '{slice.Name}'. Use Decorate for shared contributions.");
                }
            }
            _contributions.Add(contribution);
        }
    }

    internal static ApiTryComposer Create(string root) => new(root, ApiTrySliceRegistry.Slices);
    internal string Render(ApiTrySlot slot, string original = "") => string.Concat(_contributions
        .SelectMany(c => c.Outputs).Where(p => p.Key == slot).Select(p => p.Value(this, original)));
}
