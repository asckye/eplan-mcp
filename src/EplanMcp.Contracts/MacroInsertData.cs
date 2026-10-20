namespace EplanMcp.Contracts;

/// <summary>Result of <see cref="OperationNames.MacroInsertExecute"/>: the inserted devices as read back. Tags are project data.</summary>
public sealed class MacroInsertData
{
    /// <summary>The name of the project the macro was inserted in.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The full name of the page the macro was inserted on.</summary>
    public string Page { get; set; } = string.Empty;

    /// <summary>The macro's path below the macro directory, with <c>/</c> between folders.</summary>
    public string Macro { get; set; } = string.Empty;

    /// <summary>
    /// The inserted functions other than wires, one per tag, in EPLAN's order, at most
    /// <c>MacroInsertRequest.MaxDevices</c>. Tags with <c>?</c> are ones EPLAN could not number (spike S10).
    /// </summary>
    public List<PageDevice> Devices { get; set; } = [];

    /// <summary>How many distinct tags were inserted, before the list was cut.</summary>
    public int DeviceCount { get; set; }

    /// <summary>How many objects EPLAN inserted in all (functions, texts, boxes, connection points…).</summary>
    public int ObjectCount { get; set; }

    /// <summary>False when EPLAN could not read the page's size (a new, empty page), so the outline was not checked against it.</summary>
    public bool PageSizeKnown { get; set; }

    /// <summary>A problem after the commit, for example an undo step EPLAN did not close; null when there was none.</summary>
    public string? Warning { get; set; }
}
