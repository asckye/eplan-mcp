namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads EPLAN's system messages, implemented by the EPLAN adapter layer. Called on the main thread only, at most
/// once per operation: each call creates EPLAN's message collection, and reading a message of one collection after
/// another was created crashes inside EPLAN (api-notes A11).
/// </summary>
public interface ISystemMessageReader
{
    /// <summary>Every system message of this EPLAN session, Trace included, in the order EPLAN enumerates them.</summary>
    public IReadOnlyList<SystemMessageRecord> ReadAll();
}
