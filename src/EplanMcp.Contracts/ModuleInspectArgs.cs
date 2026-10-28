namespace EplanMcp.Contracts;

/// <summary>Internal read request for an explicitly selected reference-copy scope.</summary>
public sealed class ModuleInspectArgs
{
    /// <summary>The open read-only reference copy's local .elk path.</summary>
    public string Project { get; set; } = string.Empty;
    /// <summary>The bundled window macro's local path, held read-only during inspection.</summary>
    public string Macro { get; set; } = string.Empty;
    /// <summary>The exact lowercase SHA-256 of the macro.</summary>
    public string Sha256 { get; set; } = string.Empty;
    /// <summary>One to fifty distinct full page names; closure endpoints are never clipped to these pages.</summary>
    public List<string> Pages { get; set; } = [];
}
