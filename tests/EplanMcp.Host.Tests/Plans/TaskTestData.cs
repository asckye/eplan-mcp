using EplanMcp.Contracts;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Tests.Safety;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Plans;

internal static class TaskTestData
{
    public static ManualTime Clock() => new(PlanTestData.Created);

    // These streams never touch STDIO or any external resource. Each SDK instance is a separate connection.
#pragma warning disable CA2000 // McpServer takes ownership of the transport and disposes it with the session.
    public static McpServer Server() => McpServer.Create(new StreamServerTransport(Stream.Null, Stream.Null), new()
    {
        ServerInfo = new() { Name = "same-unverified-client", Version = "1" },
    });
#pragma warning restore CA2000

    public static PlanResult Result(string execution = ExecutionState.Committed, string? verification = "passed", string? reason = null) =>
        PlanResult.Aggregate([new(0, "page.update", true, execution,
            reason is null ? null : new(reason, "Check the plan result.", true))]) with
        { Verification = verification };
}
