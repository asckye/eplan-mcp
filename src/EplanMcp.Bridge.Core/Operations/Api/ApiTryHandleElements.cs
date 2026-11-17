using System.Collections;
namespace EplanMcp.Bridge.Core.Operations;
/// <summary>Bounded enumeration inside the existing trial; ownership checks run for every non-null element.</summary>
public static class ApiTryHandleElements
{
    /// <summary>Maps at most 200 source elements without advancing beyond that bound; always releases the enumerator.</summary>
    public static T[] Map<T>(IEnumerable source, Func<object, T> capture)
    {
        var result = new List<T>();
        IEnumerator iterator = source.GetEnumerator();
        try
        {
            for (int i = 0; i < 200 && iterator.MoveNext(); i++)
            { if (iterator.Current is { } item) { result.Add(capture(item)); } }
        }
        finally { (iterator as IDisposable)?.Dispose(); }
        return [.. result];
    }
}
