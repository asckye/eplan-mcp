namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads EPLAN's system messages, implemented by the EPLAN adapter layer. Called on the main thread only, at most
/// one collection at a time: each call finishes copying records before another begins; reading a message of an old
/// collection after another was created crashes inside EPLAN (api-notes A11).
/// </summary>
public interface ISystemMessageReader
{
    /// <summary>Every system message of this EPLAN session, Trace included, in the order EPLAN enumerates them.</summary>
    public IReadOnlyList<SystemMessageRecord> ReadAll();
}
