namespace EplanMcp.Workbench.Protocol;

/// <summary>
/// The peer broke the protocol: a malformed or oversized frame, or a message that strict reading rejects. The
/// connection cannot continue. An <see cref="IOException"/>, so a caller handles it as a broken connection.
/// </summary>
public sealed class WorkbenchProtocolException : IOException
{
    public WorkbenchProtocolException()
    {
    }

    public WorkbenchProtocolException(string message)
        : base(message)
    {
    }

    public WorkbenchProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
