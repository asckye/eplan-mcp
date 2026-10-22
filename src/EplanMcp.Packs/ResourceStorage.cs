namespace EplanMcp.Packs;

public enum ResourceStorage
{
    /// <summary>The file is in the pack, at the resource's path.</summary>
    Bundled,

    /// <summary>
    /// The file stays where it is (for example a company macro library); the pack records only its hash, and a check
    /// cannot verify it from the pack alone.
    /// </summary>
    Referenced,
}
