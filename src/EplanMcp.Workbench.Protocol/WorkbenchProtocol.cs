namespace EplanMcp.Workbench.Protocol;

/// <summary>
/// Constants of the Host-Workbench protocol (ADR-0014 decision 3): JSON-RPC 2.0 in <c>Content-Length</c> frames, as
/// between Host and Bridge (ADR-0003). The Workbench is the pipe server; the Host connects once per question.
/// </summary>
public static class WorkbenchProtocol
{
    /// <summary>The protocol version this build speaks. Bumped on any incompatible change.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The oldest version still spoken: N-1 once there is a version 2.</summary>
    public const int MinimumSupportedVersion = 1;

    /// <summary>
    /// The pipe name without its digest (<see cref="WorkbenchPipeName"/>). Its <c>1</c> numbers the naming scheme, not
    /// the protocol, whose version the handshake agrees on: a Host and a Workbench a version apart still find each other.
    /// </summary>
    public const string PipePrefix = "EplanMcp.Workbench.1.";

    /// <summary>Largest frame body either side sends or accepts, in bytes; a larger one is a protocol error.</summary>
    public const int MaxFrameBytes = 1024 * 1024;
}
