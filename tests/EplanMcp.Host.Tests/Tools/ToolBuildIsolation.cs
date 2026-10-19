namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// Test classes that need the tools' exact schemas build them while no other test runs. Building the tools in several
/// service providers at once races inside ModelContextProtocol 2.2 / Microsoft.Extensions.AI: a service parameter such
/// as <c>IBridgeClient bridge</c> can show up in the input schema, or the build throws "JsonSerializerOptions instance
/// is read-only". <c>serve</c> builds the tools once, on one thread, so only parallel tests see this.
/// </summary>
[CollectionDefinition(DisableParallelization = true)]
public sealed class ToolBuildIsolation;
