namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.OutcomeGet"/>.</summary>
public sealed class OutcomeGetArgs
{
    /// <summary>The request ID (<c>correlationId</c>) the Host gave the write's <c>invoke</c>.</summary>
    public string RequestId { get; set; } = string.Empty;
}
