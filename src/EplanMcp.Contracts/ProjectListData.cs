namespace EplanMcp.Contracts;

/// <summary>Result data of <see cref="OperationNames.ProjectList"/>.</summary>
public sealed class ProjectListData
{
    /// <summary>All projects open in EPLAN, in EPLAN's order; empty when none is open.</summary>
    public List<OpenProject> Projects { get; set; } = [];
}
