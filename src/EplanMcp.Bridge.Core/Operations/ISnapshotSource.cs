namespace EplanMcp.Bridge.Core.Operations;

/// <summary>One project being read for a snapshot, within one call on the main thread. Every value is raw.</summary>
public interface ISnapshotSource : IDisposable
{
    /// <summary>The project's name.</summary>
    public string Name { get; }

    /// <summary>The project's path without extension, as EPLAN reports it.</summary>
    public string Path { get; }

    /// <summary><c>PROJ_GUID</c> (api-notes A34), or null.</summary>
    public string? ProjectGuid { get; }

    /// <summary>Raw <c>PROJ_LASTMODIFICATIONDATE</c> (A36), or null.</summary>
    public string? ModifiedDate { get; }

    /// <summary>Raw <c>PROJ_LASTMODIFICATIONTIME</c> (A36), or null.</summary>
    public string? ModifiedTime { get; }

    /// <summary>Every page's <c>ObjectIdentifier</c> and full name, in any order.</summary>
    public IReadOnlyList<KeyValuePair<long, string>> Pages { get; }

    /// <summary>How many connections of the project a snapshot leaves out, 3D ones and ones on no page; known once a page was read.</summary>
    public int SkippedConnections { get; }

    /// <summary>
    /// The page with <paramref name="pageOid"/> first, then the functions placed on it and the connections on it, each
    /// with its registered properties of <paramref name="profile"/>.
    /// </summary>
    public IReadOnlyList<SnapshotSourceObject> ReadPage(long pageOid, string profile);
}
