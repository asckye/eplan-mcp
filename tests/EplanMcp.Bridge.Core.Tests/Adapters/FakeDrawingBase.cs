namespace Eplan.EplApi.Base;

// Test-only API surface: a linked production adapter runs against borrowed objects whose disposal fails.
internal readonly record struct PointD(double X, double Y);

internal sealed class BaseException : Exception;

internal sealed class MultiLangString(string content) : IDisposable
{
    public bool Disposed { get; private set; }
    public string GetAsString() => content;
    public void Dispose() => Disposed = true;
}
