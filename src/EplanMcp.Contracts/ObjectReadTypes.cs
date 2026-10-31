namespace EplanMcp.Contracts;

/// <summary>Reviewed object reads and runtime function types sharing the Function dictionary (api-notes A60).</summary>
public static class ObjectReadTypes
{
    /// <summary>Additional query names; these do not introduce property dictionaries.</summary>
    public static IReadOnlyList<string> FunctionTypes { get; } = Array.AsReadOnly(new[] { "Terminal", "PLC", "BlackBox", "PLCTerminal" });

    /// <summary>The property dictionary used for classification and formal names.</summary>
    public static string DictionaryType(string objectType) => objectType is "Terminal" or "PLC" or "BlackBox" or "BoxedDevice" or "PLCTerminal" ? "Function" : objectType;

    /// <summary>Whether a reviewed, read-only enumeration and property adapter exists.</summary>
    public static bool Supported(string objectType) => objectType is "Page" or "Function" or "Connection" or "Terminal" or "TerminalStrip"
        or "PLC" or "Cable" or "PlaceHolder" or "LocationBox" or "InterruptionPoint" or "PotentialDefinition" or "BlackBox" or "PLCTerminal" or "MacroBox";

    /// <summary>Why an otherwise known dictionary type cannot yet be read safely through a locator.</summary>
    public static string UnsupportedReason(string objectType) => objectType switch
    {
        "ArticleReference" => "transient_reference: parent/reference-position identity needs a reviewed locator adapter; use device or connection part properties",
        "PlcIO" => "unbooked_address_entry: not a PLC terminal; use PLCTerminal for placed connection points",
        "MacroEntry" => "macro_navigator_entry: project enumeration and fixture coverage have not been reviewed; placed MacroBox is supported",
        "DeletedObjectInfo" => "revision_marker: enumeration and revision fixture coverage have not been reviewed; use eplan_revision_list for revision state",
        _ => "not_implemented: no reviewed enumeration/property adapter for this dictionary type",
    };
}
