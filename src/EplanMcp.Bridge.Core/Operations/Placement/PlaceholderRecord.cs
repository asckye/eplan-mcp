using EplanMcp.Contracts.Generated;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>One placeholder object as <see cref="IPlaceholderReader"/> reads it (S29, api-notes A33). Project data.</summary>
public sealed class PlaceholderRecord
{
    /// <summary>
    /// The properties catalog/wrappers.yaml selects (name, value set and variable counts, variable names), copied by the
    /// generated reader.
    /// </summary>
    public PlaceHolderProperties Properties { get; set; } = new();

    /// <summary>The full name of the page it is on; null when EPLAN gives none.</summary>
    public string? Page { get; set; }

    /// <summary>The names of its value sets, in EPLAN's order.</summary>
    public List<string> ValueSets { get; set; } = [];

    /// <summary>How many objects it references.</summary>
    public int AssignedObjects { get; set; }

    /// <summary>Each value set's value of each variable, by value set and then variable.</summary>
    public List<PlaceholderValueRecord> Values { get; set; } = [];

    /// <summary>Its current values, one per referenced property, in EPLAN's order; null when not read or refused.</summary>
    public List<PlaceholderText>? CurrentValues { get; set; }

    /// <summary>Whether EPLAN refused the current values because the placeholder was not locked (api-notes A33).</summary>
    public bool CurrentValuesRefused { get; set; }

    /// <summary>The name as the tools give it: empty when EPLAN gives none.</summary>
    public string Name => Properties.Name ?? string.Empty;
}
