namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Reference-copy lifecycle, on the main thread inside the dispatcher's LockingStep (A31/A34/A46).</summary>
public interface IReferenceProjects
{
    /// <summary>Snapshot-copies a closed .elk or restores a .zw1 into the checked empty destination.</summary>
    public void Copy(string source, string destination);
    /// <summary>Opens only if absent, read-only; returns the opened database identity, not PROJ_GUID (A34).</summary>
    public uint OpenReadOnly(string path);
    /// <summary>Closes this exact database at this path; refuses a replacement opened by the engineer.</summary>
    public void Close(string path, uint database);
}
