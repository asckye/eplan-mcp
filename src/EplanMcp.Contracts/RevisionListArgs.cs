namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.RevisionList"/> (docs/tools/eplan_revision_list.md); the paging applies to the revision records.</summary>
public sealed class RevisionListArgs : ListArgs
{
    /// <summary>Language of the revision descriptions, for example <c>zh_CN</c>; null means EPLAN's user interface language with fallback.</summary>
    public string? Language { get; set; }
}
