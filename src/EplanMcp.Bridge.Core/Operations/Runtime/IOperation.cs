using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// One Bridge operation (<c>invoke</c> target). Arguments are parsed on the pipe thread; <see cref="Execute"/> runs
/// on EPLAN's main thread, inside the dispatcher's LockingStep, and must return contract DTOs only, never EPLAN
/// objects (coding standards 9.1).
/// </summary>
public interface IOperation
{
    /// <summary>One of <see cref="EplanMcp.Contracts.OperationNames"/>.</summary>
    public string Name { get; }

    /// <summary>Validates and converts the raw arguments. Throws <see cref="BridgeErrorException"/> with INVALID_ARGUMENT.</summary>
    public object ParseArgs(JToken? args);

    /// <summary>Does the work on the main thread and returns the result data.</summary>
    public object? Execute(object args);
}
