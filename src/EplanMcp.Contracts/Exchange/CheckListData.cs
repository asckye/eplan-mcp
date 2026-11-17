namespace EplanMcp.Contracts;

/// <summary>Result data of <see cref="OperationNames.CheckList"/>: <see cref="ListData{TItem}"/> plus counts.</summary>
public sealed class CheckListData
{
    /// <summary>The name of the project actually used.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The requested slice, in EPLAN's order.</summary>
    public List<CheckMessage> Items { get; set; } = [];

    /// <summary>How many messages match all filters, before slicing.</summary>
    public int Total { get; set; }

    /// <summary>Errors before the severity filter; done ones only with <see cref="CheckListArgs.IncludeDone"/>.</summary>
    public int Errors { get; set; }

    /// <summary>Warnings, counted like <see cref="Errors"/>.</summary>
    public int Warnings { get; set; }

    /// <summary>Hints, counted like <see cref="Errors"/>.</summary>
    public int Hints { get; set; }

    /// <summary>How many of the project's check messages are marked as done, listed or not.</summary>
    public int Done { get; set; }
}
