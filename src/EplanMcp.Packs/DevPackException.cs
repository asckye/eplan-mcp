namespace EplanMcp.Packs;

/// <summary>A development pack that was not changed, with <see cref="Error"/> saying why and every problem found.</summary>
public sealed class DevPackException : Exception
{
    public DevPackException()
        : this(DevPackError.Invalid, [])
    {
    }

    public DevPackException(string message)
        : this(message, null)
    {
    }

    public DevPackException(string message, Exception? innerException)
        : base(message, innerException)
    {
        Problems = [new PackProblem("pack", message)];
    }

    public DevPackException(DevPackError error, IReadOnlyList<PackProblem> problems)
        : base(string.Join("; ", problems))
    {
        Error = error;
        Problems = problems;
    }

    public DevPackError Error { get; }

    public IReadOnlyList<PackProblem> Problems { get; }
}
