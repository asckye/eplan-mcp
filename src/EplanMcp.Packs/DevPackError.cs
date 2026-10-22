namespace EplanMcp.Packs;

/// <summary>Why a development pack was not changed.</summary>
public enum DevPackError
{
    /// <summary>The pack, or the pack the change would give, fails its check (PACK_INVALID).</summary>
    Invalid,

    /// <summary>A resource with the same path and other content is in the pack already (ALREADY_EXISTS).</summary>
    Conflict,

    /// <summary>Another EplanMcp is changing the pack.</summary>
    Busy,
}
