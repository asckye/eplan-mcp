namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.CheckList"/> (docs/tools/eplan_check_list.md).</summary>
public sealed class CheckListArgs : ListArgs
{
    /// <summary>
    /// <see cref="CheckMessage.Error"/>, <see cref="CheckMessage.Warning"/> or <see cref="CheckMessage.Hint"/>, ignoring
    /// case; null or blank lists every severity.
    /// </summary>
    public string? Severity { get; set; }

    /// <summary>Whether messages marked as done in EPLAN are listed and counted.</summary>
    public bool IncludeDone { get; set; }
}
