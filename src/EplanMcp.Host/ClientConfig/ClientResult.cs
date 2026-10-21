namespace EplanMcp.Host.ClientConfig;

internal enum ClientConfigMode
{
    Write,
    DryRun,
    Remove,
}

/// <summary>What client-config is asked to do; no clients (with <see cref="ClientConfigMode.Remove"/> only) means every recorded entry.</summary>
internal sealed record ClientConfigRequest(IReadOnlyList<McpClient> Clients, ClientConfigMode Mode);

internal enum ClientAction
{
    Write,
    Remove,
}

internal enum ClientOutcome
{
    Written,
    WouldWrite,
    Removed,
    Unchanged,
    Failed,
}

/// <summary>A problem and what was found, in English (file paths, parser messages).</summary>
internal sealed record ClientIssue(ClientProblem Problem, string Detail);

/// <summary>
/// One client's result. <see cref="Before"/> and <see cref="After"/> show only the entry; <see cref="Problem"/> is set
/// when it failed, and also when the file was written but the record could not be updated.
/// </summary>
internal sealed record ClientResult(string Client, string? File, ClientAction Action, ClientOutcome Outcome)
{
    public string? Before { get; init; }

    public string? After { get; init; }

    public string? Backup { get; init; }

    public bool CommentsDropped { get; init; }

    public ClientIssue? Problem { get; init; }
}

/// <summary>A whole run: the server the entries start, a problem that stopped every client, and each client's result.</summary>
internal sealed record ClientConfigReport(LocalServer Server, ClientConfigMode Mode, ClientIssue? Problem, IReadOnlyList<ClientResult> Clients)
{
    public bool Failed => Problem is not null || Clients.Any(c => c.Problem is not null);
}
