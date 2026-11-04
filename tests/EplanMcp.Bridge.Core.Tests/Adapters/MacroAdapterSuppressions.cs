using System.Diagnostics.CodeAnalysis;

// These linked adapters target .NET Framework 4.8, where the suggested string overloads are unavailable.
[assembly: SuppressMessage("Globalization", "CA1307:Specify StringComparison for clarity",
    Scope = "member", Target = "~M:EplanMcp.Bridge.Eplan.Adapters.EplanMacroReader.MacroRoot~System.String",
    Justification = "The linked production adapter targets .NET Framework 4.8.")]
[assembly: SuppressMessage("Usage", "CA2249:Consider using String.Contains instead of String.IndexOf",
    Scope = "member", Target = "~M:EplanMcp.Bridge.Eplan.Adapters.EplanMacroInserter.Insert(System.String,System.String,System.String,System.Int32,System.Double,System.Double,System.Boolean,System.String,System.String,System.String,System.Boolean)~EplanMcp.Bridge.Core.Operations.PlacedMacro",
    Justification = "The linked production adapter targets .NET Framework 4.8.")]
