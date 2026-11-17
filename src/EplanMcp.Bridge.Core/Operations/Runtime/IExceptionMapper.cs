using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Turns an exception thrown on the main thread into a <see cref="BridgeError"/>. The EPLAN adapter layer knows the
/// EPLAN exception types (LockingException → LOCK_FAILED, …, coding standards 9.6); Bridge.Core does not.
/// Returns null for exceptions it does not recognise, which become INTERNAL.
/// </summary>
public interface IExceptionMapper
{
    public BridgeError? Map(Exception exception);
}
