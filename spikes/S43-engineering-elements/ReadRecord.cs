using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S43;

// A read failure belongs to its evidence field, never to the next element's placement transaction.
internal sealed class ReadRecord
{
    internal JObject Data { get; } = new();
    private readonly JArray _errors = [];
    internal JToken? ErrorFor(string field) => _errors.FirstOrDefault(e => (string?)e["field"] == field)?["error"]?.DeepClone();
    internal T Get<T>(string field, Func<T> read, T fallback = default!)
    {
        try { return read(); }
        catch (Exception ex) { Error(field, ex); return fallback; }
    }
    internal void Field(string field, Func<object?> read)
    {
        Data[field] = Get<JToken?>(field, () =>
        {
            object? value = read();
            return value is null ? null : value is JToken token ? token : JToken.FromObject(value);
        });
    }
    internal void Array<T>(string field, Func<IEnumerable<T>?> read, Func<T, JToken?> item)
    {
        IEnumerable<T>? values = Get(field, read);
        if (values is null) { Data[field] = null; return; }
        var result = new JArray();
        Get<object?>(field, () =>
        {
            int index = 0;
            foreach (T value in values)
            {
                string path = field + "[" + Evidence.Number(index++) + "]";
                result.Add((value is null ? null : Get(path, () => item(value))) ?? JValue.CreateNull());
            }
            return null;
        });
        Data[field] = result;
    }
    internal void Error(string field, Exception ex)
    {
        _errors.Add(new JObject { ["field"] = field, ["error"] = Evidence.Error(ex) });
        Data["readErrors"] = _errors;
    }
}
