using System.Globalization;
using System.Text;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>One bounded observation of affected pages, or unpaged objects, with detached identity deduplication.</summary>
internal sealed class ApiTryLifecycleScan(ApiTryArgs args, Func<TimeSpan> elapsed)
{
    private readonly HashSet<(int, long)> _touched = new(ApiTryPlanOperation.Parameters(args.Parameters).Descendants()
        .OfType<JObject>().Where(p => p["oid"] is not null).Select(p => ((int)p["type"]!, (long)p["oid"]!)));
    private readonly Dictionary<string, string> _state = new(StringComparer.Ordinal);
    private readonly string[]? _pages = ApiTryObservations.Pages(args);
    private int _visited;
    private int _bytes;

    internal bool Touched(DrawingLocator locator) => _touched.Contains((locator.Type, locator.Oid));

    internal Dictionary<string, string> Read<T>(Func<string[]?, Action, IEnumerable<T>> source, Func<T, string?> key, Func<T, string> describe)
    {
        Check();
        foreach (T obj in source(_pages, Check))
        {
            _visited++;
            Check();
            string? identity = key(obj);
            if (identity is null || _state.ContainsKey(identity))
            { continue; }
            string description = describe(obj);
            _state.Add(identity, description);
            _bytes += Encoding.UTF8.GetByteCount(description);
            Check();
        }
        Check();
        return _state;
    }

    internal Dictionary<string, string> Measurements() => new()
    {
        ["scope"] = _pages is { } pages ? "pages:" + pages.Length.ToString(CultureInfo.InvariantCulture) : "project",
        ["visited"] = _visited.ToString(CultureInfo.InvariantCulture),
        ["objects"] = _state.Count.ToString(CultureInfo.InvariantCulture),
        ["bytes"] = _bytes.ToString(CultureInfo.InvariantCulture),
        ["elapsedMs"] = elapsed().TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
    };

    private void Check()
    {
        OperationContext.Current.ThrowIfCancellationRequested();
        if (elapsed().TotalSeconds > 20 || _state.Count > 10000 || _bytes > 262144)
        { throw new BridgeErrorException(ErrorReason.Timeout, "Lifecycle observations exceeded their budget; select explicit page or object locators to narrow the trial.", metadata: Measurements()); }
    }
}
