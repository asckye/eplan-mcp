namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Opens <see cref="ITransactionScope"/>s, implemented by the EPLAN adapter layer. Main thread only.</summary>
public interface ITransactionScopeFactory
{
    /// <summary>
    /// Opens a transaction whose undo step EPLAN lists as <paramref name="undoDescription"/>. Leaves nothing open when
    /// it throws.
    /// </summary>
    public ITransactionScope Begin(string undoDescription);
}
