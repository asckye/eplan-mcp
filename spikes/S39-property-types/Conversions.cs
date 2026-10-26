using System.Globalization;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S39;

internal static class Conversions
{
    internal static JObject Read(PropertyValue value, int? index)
    {
        var row = new JObject { ["index"] = index, ["isEmpty"] = value.IsEmpty };
        if (row.Value<bool>("isEmpty")) { return row; } // A36/A42: no conversion on an empty value.
        var outcomes = new JObject();
        row["conversions"] = outcomes;
        void Probe(string name, Func<object?> read)
        {
            try { outcomes[name] = new JObject { ["status"] = "returned", ["value"] = JToken.FromObject(read() ?? JValue.CreateNull()) }; }
            catch (Exception ex) { outcomes[name] = new JObject { ["status"] = "exception", ["exceptionType"] = ex.GetType().FullName }; }
        }
        Probe("ToString()", () => value.ToString());
        Probe("ToString(en_US)", () => value.ToString(ISOCode.Language.L_en_US));
        Probe("ToString(en_US, <S39 fallback>)", () => value.ToString(ISOCode.Language.L_en_US, "<S39 fallback>"));
        Probe("ToBool()", () => value.ToBool());
        Probe("ToInt()", () => value.ToInt());
        Probe("ToDouble()", () => value.ToDouble());
        Probe("ToPointD()", () => Point(value.ToPointD()));
        Probe("ToTime()", () => Time(value.ToTime()));
        Probe("ToMultiLangString()", () => Text(value.ToMultiLangString()));
        Probe("GetDisplayString()", () => Text(value.GetDisplayString()));
        // The catalog also exposes these seven outward implicit conversions; test them independently.
        Probe("implicit String", () => (string)value);
        Probe("implicit Bool", () => (bool)value);
        Probe("implicit Int32", () => (int)value);
        Probe("implicit Double", () => (double)value);
        Probe("implicit PointD", () => Point(value));
        Probe("implicit DateTime", () => Time(value));
        Probe("implicit MultiLangString", () => Text(value));
        row["display"] = outcomes["ToString()"]!["value"]?.DeepClone();
        return row;
    }
    private static JObject Point(PointD point) => new() { ["x"] = point.X, ["y"] = point.Y };
    private static JObject Time(DateTime time) => new()
    { ["roundTrip"] = time.ToString("o", CultureInfo.InvariantCulture), ["kind"] = time.Kind.ToString(), ["ticks"] = time.Ticks };
    private static JObject Text(MultiLangString text)
    {
        using (text)
        {
            var translations = new JObject();
            var languages = new LanguageList(); // ArrayList, not IDisposable (A42).
            int count = text.GetLanguageList(ref languages);
            for (int i = 0; i < count; i++)
            {
                ISOCode.Language language = languages.get_Language(i);
                using var code = new ISOCode(language);
                translations[code.GetString()] = text.GetString(language);
            }
            return new JObject { ["raw"] = text.ToString(), ["translations"] = translations };
        }
    }
}
