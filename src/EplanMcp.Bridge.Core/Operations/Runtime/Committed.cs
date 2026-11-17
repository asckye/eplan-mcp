namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// A committed write's result and, when something went wrong after the commit (the undo step did not close), a
/// warning for the tool result (ADR-0010 section 1).
/// </summary>
public sealed class Committed<T>(T result, string? warning)
{
    public T Result { get; } = result;

    public string? Warning { get; } = warning;
}
