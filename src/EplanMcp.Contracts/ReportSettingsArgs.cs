namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.ReportSettingsPlan"/> and <see cref="OperationNames.ReportSettingsExecute"/> (docs/tools/eplan_report_configure.md).</summary>
public sealed class ReportSettingsArgs
{
    /// <summary>The project settings a call may change, under EPLAN's parts list settings. No tool or configuration can add entries.</summary>
    public static readonly IReadOnlyList<string> Allowed =
    [
        "PartsListGui.SummarizePartsOfSameDT",
        "PartsListGui.DissectAssemblies",
        "PartsListGui.IncludeTerminalStripParts",
        "PartsListGui.IncludePlugStripParts",
    ];

    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>The settings to change, by name in <see cref="Allowed"/>.</summary>
    public Dictionary<string, bool>? Settings { get; set; }

    /// <summary>Plan state fingerprint, required for execution.</summary>
    public string? Fingerprint { get; set; }
}

/// <summary>Data of <see cref="OperationNames.ReportSettingsExecute"/>.</summary>
public sealed class ReportSettingsData
{
    public string Project { get; set; } = string.Empty;
}
