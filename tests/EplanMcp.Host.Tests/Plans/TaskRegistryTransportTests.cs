using System.Net;
using EplanMcp.Contracts;
using EplanMcp.Host.Http;
using EplanMcp.Host.Plans;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Medium")]
public sealed class TaskRegistryTransportTests
{
    [Fact]
    public async Task StatelessHttp_RefusesCreationAndReads_EvenWithClientNameAddressAndTaskId()
    {
        var clock = TaskTestData.Clock();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Items["mcpClient"] = "trusted-client";
        var caller = new HttpCaller(new HttpContextAccessor { HttpContext = context });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock).AddSingleton(caller);
        services.AddEplanMcp().WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless);
        await using ServiceProvider provider = services.BuildServiceProvider();
        TaskRegistry registry = provider.GetRequiredService<TaskRegistry>();
        await using McpServer server = TaskTestData.Server();
        Assert.NotNull(caller.Name);
        foreach (Action operation in new Action[]
        {
            () => registry.Create(server, "plan", "PROJECT", 1),
            () => registry.Get(server, "known-task-id"),
            () => registry.OwnerFor(server),
        })
        {
            TaskError error = Assert.Throws<TaskRegistryException>(operation).Error;
            Assert.Equal(ErrorReason.CapabilityMissing, error.Reason);
            Assert.False(error.Retryable);
            Assert.Equal("transport:stable_session", error.Metadata!.Value.GetProperty("missing")[0].GetString());
        }
        TaskRegistryException waiting = await Assert.ThrowsAsync<TaskRegistryException>(() => registry.WaitAsync(server, "known-task-id", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(ErrorReason.CapabilityMissing, waiting.Error.Reason);
    }
}
