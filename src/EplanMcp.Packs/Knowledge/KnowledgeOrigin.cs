using System.ComponentModel;

namespace EplanMcp.Packs.Knowledge;

[Description("Where a knowledge document comes from (M6 design 4.1.3), so engineers and agents can go back to it.")]
public sealed record KnowledgeOrigin
{
    [Description("An asset: '<kind>/<name>' in this pack, or a full asset id '<pack>@<version>:<kind>/<name>' of another.")]
    public string? Asset { get; init; }

    [Description("A fact of this pack: 'fact:<resource path>#<fact name>'.")]
    public string? Fact { get; init; }

    [Description("A page of a project, such as =AP+ST1/3.")]
    public string? Page { get; init; }

    public string? Note { get; init; }
}
