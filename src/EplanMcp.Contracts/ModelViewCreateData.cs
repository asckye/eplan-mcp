namespace EplanMcp.Contracts;

/// <summary>Result of <see cref="OperationNames.ModelViewCreateExecute"/>: the inserted model view as read back.</summary>
public sealed class ModelViewCreateData
{
    /// <summary>The project.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The page the view was inserted on.</summary>
    public string Page { get; set; } = string.Empty;

    /// <summary>The view's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>EPLAN's object ID of the model view (OBJECTID).</summary>
    public string ObjectId { get; set; } = string.Empty;

    /// <summary>Placements on the page after the insertion minus before.</summary>
    public int Added { get; set; }

    /// <summary>A note from the transaction, e.g. that undo could not be grouped; null when none.</summary>
    public string? Warning { get; set; }
}
