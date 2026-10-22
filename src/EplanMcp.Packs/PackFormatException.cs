namespace EplanMcp.Packs;

/// <summary>
/// A pack, or one of its files, that EplanMcp refuses to read. The message names the problem in words an engineer can
/// act on and never a local absolute path.
/// </summary>
public sealed class PackFormatException : Exception
{
    public PackFormatException()
    {
    }

    public PackFormatException(string message)
        : base(message)
    {
    }

    public PackFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
