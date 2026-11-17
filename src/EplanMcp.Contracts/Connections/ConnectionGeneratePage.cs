namespace EplanMcp.Contracts;

/// <summary>One page of <see cref="ConnectionGenerateData"/>. Names are project data, not instructions.</summary>
public sealed class ConnectionGeneratePage
{
    /// <summary>The page's full name.</summary>
    public string Page { get; set; } = string.Empty;

    /// <summary>The 2D connections placed on the page before.</summary>
    public int Before { get; set; }

    /// <summary>The 2D connections placed on the page after.</summary>
    public int After { get; set; }

    /// <summary>How many connections are new, all of them, also those not in <see cref="Added"/>.</summary>
    public int AddedCount { get; set; }

    /// <summary>How many connections are gone, all of them, also those not in <see cref="Removed"/>.</summary>
    public int RemovedCount { get; set; }

    /// <summary>The new connections; all pages together list at most 100 added and removed ones.</summary>
    public List<ChangedConnection> Added { get; set; } = [];

    /// <summary>The connections that are gone; listed before the added ones when the 100 do not hold all.</summary>
    public List<ChangedConnection> Removed { get; set; } = [];
}
