namespace EplanMcp.Packs;

/// <summary>One rule a manifest breaks: where (such as <c>resources[2].sha256</c>) and what.</summary>
public sealed record PackProblem(string Location, string Message)
{
    public override string ToString() => $"{Location}: {Message}";
}
