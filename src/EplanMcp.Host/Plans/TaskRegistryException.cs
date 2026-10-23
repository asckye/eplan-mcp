namespace EplanMcp.Host.Plans;

/// <summary>A local registry failure, converted to a tool error only at the future MCP boundary.</summary>
internal sealed class TaskRegistryException(TaskError error) : Exception(error.Message)
{
    public TaskError Error { get; } = error;
}
