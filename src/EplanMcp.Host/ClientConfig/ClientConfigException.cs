namespace EplanMcp.Host.ClientConfig;

/// <summary>Why <c>eplanmcp client-config</c> leaves a client's configuration alone.</summary>
internal enum ClientProblem
{
    /// <summary>The file cannot be read as its format, or holds the servers in a form that cannot be changed safely.</summary>
    InvalidFile,

    /// <summary>Codex's TOML defines the server in a form that a line-by-line edit cannot change safely.</summary>
    UnsupportedToml,

    /// <summary>Several Claude Desktop packages are installed, and none is the known one.</summary>
    AmbiguousPackage,

    /// <summary>The file changed between reading it and writing it.</summary>
    FileChanged,

    /// <summary>The record of written entries (<c>state\clients.json</c>) cannot be read.</summary>
    InvalidRecord,
}

/// <summary>A client whose configuration is not changed; <see cref="Detail"/> names what was found, in English.</summary>
internal sealed class ClientConfigException : Exception
{
    public ClientConfigException()
    {
    }

    public ClientConfigException(string message)
        : base(message)
    {
    }

    public ClientConfigException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ClientConfigException(ClientProblem problem, string detail, Exception? innerException = null)
        : base($"{problem}: {detail}", innerException)
    {
        Problem = problem;
        Detail = detail;
    }

    public ClientProblem Problem { get; }

    public string Detail { get; } = string.Empty;
}
