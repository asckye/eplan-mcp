namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.LanguageRemovePlan"/> and <see cref="OperationNames.LanguageRemoveExecute"/>.</summary>
public sealed class LanguageRemoveArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>The languages to remove, 1 to 10 codes such as <c>sv_SE</c>: translation languages, not the source language.</summary>
    public List<string> Languages { get; set; } = [];

    /// <summary>For the execute operation: the plan's <see cref="WritePlan.Fingerprint"/>. Null for the plan.</summary>
    public string? Fingerprint { get; set; }

    /// <summary>For the execute operation: where to back the project up first (ADR-0013). Null for the plan.</summary>
    public BackupTarget? Backup { get; set; }
}
