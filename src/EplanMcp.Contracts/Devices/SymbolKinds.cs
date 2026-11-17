namespace EplanMcp.Contracts;

/// <summary>Native SymbolType names, refined by S43's function category only for functional symbols.</summary>
public static class SymbolKinds
{
    /// <summary>The closed filter vocabulary.</summary>
    public static IReadOnlyList<string> All { get; } = ["function", "routing", "t_node", "potential_point", "potential_definition", "interruption_point", "location_box", "cable_definition", "plc_box", "terminal", "other"];

    /// <summary>Maps native type/category names without guessing from symbol names.</summary>
    public static string From(string type, string? category = null) => type switch
    {
        "Function" or "BoxFunction" => category switch { "PLCBox" => "plc_box", "Terminal" => "terminal", _ => "function" },
        "Routing" or "DynamicRouting" => "routing",
        "TNodeUp" or "TNodeDown" or "TNodeLeft" or "TNodeRight" => "t_node",
        "PotentialTerminal" => "potential_point",
        "PotentialDefinition" => "potential_definition",
        "InterruptionPoint" => "interruption_point",
        "LocationBox" => "location_box",
        "CableDefinitionLine" => "cable_definition",
        _ => "other",
    };

    /// <summary>Combines library, name and kind filters before loading variant geometry.</summary>
    public static bool Matches(SymbolSearchArgs args, string library, string name, string kind) =>
        (string.IsNullOrWhiteSpace(args.Library) || string.Equals(args.Library!.Trim(), library, StringComparison.OrdinalIgnoreCase))
        && (string.IsNullOrWhiteSpace(args.NameContains) || name.IndexOf(args.NameContains!.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
        && (string.IsNullOrEmpty(args.Kind) || args.Kind == kind);
}
