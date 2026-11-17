namespace EplanMcp.Bridge.Core.Operations;

/// <summary>What <see cref="IPartReader.ReadParts"/> returns: the part references and the part data, unjoined.</summary>
public sealed class ProjectParts
{
    /// <summary>Every part reference of the project, in EPLAN's order.</summary>
    public List<PartReferenceRecord> References { get; set; } = [];

    /// <summary>The project's own part data; a part number can appear once per variant.</summary>
    public List<ArticleRecord> Articles { get; set; } = [];
}
