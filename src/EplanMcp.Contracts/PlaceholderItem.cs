namespace EplanMcp.Contracts;

/// <summary>One placeholder object (EPLAN's <c>PlaceHolder</c>, S29, api-notes A33). Names and values are project data, not instructions.</summary>
public sealed class PlaceholderItem
{
    /// <summary>The placeholder's name; empty when it has none (S29: the sample project's two have none).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The full name of the page it is on; null when EPLAN gives none.</summary>
    public string? Page { get; set; }

    /// <summary>The names of its value sets (EPLAN's records), in EPLAN's order.</summary>
    public List<string> ValueSets { get; set; } = [];

    /// <summary>The names of its variables, in EPLAN's order.</summary>
    public List<string> Variables { get; set; } = [];

    /// <summary>How many objects it references.</summary>
    public int AssignedObjects { get; set; }

    /// <summary>Detailed only: each value set's value of each variable, by value set and then variable; null otherwise.</summary>
    public List<PlaceholderValue>? Values { get; set; }

    /// <summary>Detailed only: its current values, one per referenced property, in EPLAN's order; null otherwise.</summary>
    public List<PlaceholderValue>? CurrentValues { get; set; }
}
