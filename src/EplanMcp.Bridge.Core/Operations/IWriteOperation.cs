namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// A write's <c>*.execute</c> operation (M3 design 4.1, 4.2). The router records its outcome under the request ID,
/// refuses a request ID it has seen, and stamps its errors with their execution state (ADR-0010). Its
/// <see cref="IOperation.Execute"/> makes the change inside a transaction: it returns only after the commit, and a
/// <see cref="BridgeErrorException"/> it throws carries <c>Execution</c> when it is not "rolled back".
/// </summary>
public interface IWriteOperation : IOperation
{
}
