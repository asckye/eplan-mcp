namespace EplanMcp.Packs;

public enum PackInstallOutcome
{
    Installed,

    /// <summary>The same version with the same <c>pack.json</c> was installed already; nothing changed.</summary>
    AlreadyInstalled,

    Refused,
}
