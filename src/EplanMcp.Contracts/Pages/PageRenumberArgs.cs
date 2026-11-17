namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.PageRenumberPlan"/> and <see cref="OperationNames.PageRenumberExecute"/>.</summary>
public sealed class PageRenumberArgs
{
    /// <summary>The values of <see cref="Subpages"/>, the default first.</summary>
    public static readonly IReadOnlyList<string> SubpageModes = ["retain", "consecutive", "to_main"];

    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>The full names of the pages to renumber, matched exactly: 1 to 50.</summary>
    public List<string> Pages { get; set; } = [];

    /// <summary>The first page number, at least 1.</summary>
    public int Start { get; set; }

    /// <summary>The step between page numbers, at least 1.</summary>
    public int Step { get; set; } = 1;

    /// <summary>True to keep the letters of a page number.</summary>
    public bool KeepText { get; set; }

    /// <summary>
    /// Subpages such as <c>/1.2</c>: <c>retain</c> keeps them, <c>consecutive</c> numbers them from 1, <c>to_main</c> makes
    /// them main pages; null means <c>retain</c>.
    /// </summary>
    public string? Subpages { get; set; }

    /// <summary>For the execute operation: the plan's <see cref="WritePlan.Fingerprint"/>. Null for the plan.</summary>
    public string? Fingerprint { get; set; }

    /// <summary>For the execute operation: where to back the project up first (ADR-0013). Null for the plan.</summary>
    public BackupTarget? Backup { get; set; }
}
