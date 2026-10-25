namespace EplanMcp.Contracts;

/// <summary>One pure-validation failure; callers collect the complete list before returning an error.</summary>
public sealed class PropertyViolation
{
    /// <summary>The RFC 6901 JSON Pointer in the original input.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>A shared error reason, normally INVALID_ARGUMENT or POLICY_DENIED.</summary>
    public string Reason { get; set; } = ErrorReason.InvalidArgument;

    /// <summary>A safe English explanation with no supplied values or local paths.</summary>
    public string Message { get; set; } = string.Empty;
}
