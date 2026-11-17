namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.OperationCancel"/>.</summary>
public sealed class OperationCancelArgs
{
    /// <summary>The request ID (<c>correlationId</c>) the Host gave the invoke to stop.</summary>
    public string RequestId { get; set; } = string.Empty;
}
