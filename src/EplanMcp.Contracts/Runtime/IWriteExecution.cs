namespace EplanMcp.Contracts;

/// <summary>A successful write that can discover a no-op only during execution.</summary>
public interface IWriteExecution
{
    /// <summary>Committed, or not_started after rolling back a no-op.</summary>
    public string Execution { get; }
}
