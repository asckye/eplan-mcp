namespace EplanMcp.Contracts;

/// <summary>Arguments of the translation database import plan and execution.</summary>
public sealed class TranslationImportArgs
{
    /// <summary>The file's relative path below the Host's import folder.</summary>
    public string? File { get; set; }

    /// <summary>The full path resolved by the Host under SEC-31; never returned to the model.</summary>
    public string? FilePath { get; set; }

    /// <summary>The file's SHA-256 as read by the Host, in lowercase hex.</summary>
    public string? Sha256 { get; set; }

    /// <summary>One exact language column used to match entries; defaults to en_US.</summary>
    public string ReferenceLanguage { get; set; } = "en_US";

    /// <summary>Execution only: the plan's file SHA-256.</summary>
    public string? Fingerprint { get; set; }

    /// <summary>Execution only: the configured database the plan named and the Host backed up.</summary>
    public string? Database { get; set; }
}
