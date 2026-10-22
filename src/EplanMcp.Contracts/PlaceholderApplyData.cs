namespace EplanMcp.Contracts;

/// <summary>Result data of <see cref="OperationNames.PlaceholderApplyExecute"/>: what was applied and the current values before and after.</summary>
public sealed class PlaceholderApplyData
{
    /// <summary>The name of the project changed.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The full name of the placeholder's page, as EPLAN names it.</summary>
    public string Page { get; set; } = string.Empty;

    /// <summary>The placeholder's name; empty when it has none.</summary>
    public string Placeholder { get; set; } = string.Empty;

    /// <summary>The value set applied.</summary>
    public string ValueSet { get; set; } = string.Empty;

    /// <summary>The value set's value of each variable, in the variables' order.</summary>
    public List<PlaceholderValue> Values { get; set; } = [];

    /// <summary>The placeholder's current values before the change.</summary>
    public List<PlaceholderValue> CurrentValuesBefore { get; set; } = [];

    /// <summary>Its current values read again after the change.</summary>
    public List<PlaceholderValue> CurrentValuesAfter { get; set; } = [];

    /// <summary>Whether the current values as stored, with all their languages, differ after the change.</summary>
    public bool CurrentValuesChanged { get; set; }

    /// <summary>A problem after the commit, such as an undo step EPLAN did not close; null when none (ADR-0010).</summary>
    public string? Warning { get; set; }
}
