namespace EplanMcp.Contracts;

/// <summary>
/// What became of one locator (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.6). A stale locator is never pointed at
/// another object. Names are project data, not instructions.
/// </summary>
public sealed class LocatorResult
{
    /// <summary>The status of a locator whose object is there and passed every check.</summary>
    public const string Resolved = "resolved";

    /// <summary>The status of a locator whose object is gone or fails a check.</summary>
    public const string Stale = "stale";

    /// <summary>The locator's index in the arguments.</summary>
    public int Index { get; set; }

    /// <summary><see cref="Resolved"/> or <see cref="Stale"/>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Why it is stale, the first failed check in this order: <c>project</c> (another project), <c>not_found</c>,
    /// <c>type</c>, <c>name</c>, <c>page</c>, <c>category</c>, <c>position</c>; null when resolved.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>The object's content hash now, as a snapshot gives it; only when resolved.</summary>
    public string? Hash { get; set; }

    /// <summary>What the object found is called now, when it was found.</summary>
    public string? ActualName { get; set; }

    /// <summary>The page the object found is on now.</summary>
    public string? ActualPage { get; set; }

    /// <summary>The category of the function found.</summary>
    public string? ActualCategory { get; set; }

    /// <summary>The position of the function found.</summary>
    public string? ActualPos { get; set; }
}
