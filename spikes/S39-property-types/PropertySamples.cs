using System.Diagnostics;
using Eplan.EplApi.DataModel;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S39;

internal static class PropertySamples
{
    internal static void Read(Project project, JObject report)
    {
        using var finder = new DMObjectsFinder(project);
        var samples = new JObject { ["20027"] = new JArray(), ["20011"] = new JArray() };
        var errors = new JArray();
        report["samples"] = samples;
        report["readErrors"] = errors;
        report["limitPerProperty"] = 50;
        var watch = Stopwatch.StartNew();
        int examined = 0;
        // A12/A44: include representations omitted by the unfiltered finder; do not dispose borrowed functions/pages.
        var functions = finder.GetFunctions(null).Concat(finder.GetPages(null).SelectMany(p => p.AllPlacements.OfType<Function>()))
            .GroupBy(f => f.ToStringIdentifier(), StringComparer.Ordinal).Select(g => g.First())
            .OrderBy(f => f.Name, StringComparer.Ordinal).ThenBy(f => f.ToStringIdentifier(), StringComparer.Ordinal);
        foreach (Function function in functions)
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(90)) { throw new TimeoutException("S39 reading exceeded 90 seconds."); }
            string tag = function.Name, identifier = function.ToStringIdentifier();
            examined++;
            // A36: exactly one acquisition and release per unique function, including rejected candidates.
            using UniversalPropertyList list = function.Properties;
            foreach (int number in new[] { 20027, 20011 })
            {
                var cohort = (JArray)samples[number.ToString(System.Globalization.CultureInfo.InvariantCulture)]!;
                if (cohort.Count >= 50) { continue; }
                try
                {
                    using AnyPropertyId id = number;
                    using PropertyValue parent = list[id];
                    using PropertyDefinition definition = parent.Definition;
                    bool indexed = definition.IsIndexed;
                    int[] indexes = indexed ? parent.Indexes.OrderBy(i => i).ToArray() : [];
                    var values = new JArray();
                    if (!indexed) { values.Add(Conversions.Read(parent, null)); }
                    foreach (int index in indexes)
                    {
                        using PropertyValue value = list[id, index];
                        values.Add(Conversions.Read(value, index));
                    }
                    if (!values.OfType<JObject>().Any(v => v.Value<bool?>("isEmpty") == false)) { continue; }
                    cohort.Add(new JObject
                    {
                        ["deviceTag"] = tag, ["identifier"] = identifier, ["page"] = function.Page?.Name,
                        ["isMainFunction"] = function.IsMainFunction,
                        ["typeName"] = Enum.GetName(typeof(PropertyDefinition.PropertyType), definition.Type),
                        ["typeNumber"] = (int)definition.Type, ["isIndexed"] = indexed,
                        ["storedIndexes"] = new JArray(indexes), ["values"] = values,
                    });
                }
                catch (PropertyNotFoundException) { /* A36: this property is not applicable to this function. */ }
                catch (Exception ex) { errors.Add(new JObject { ["deviceTag"] = tag, ["identifier"] = identifier, ["propertyId"] = number, ["exceptionType"] = ex.GetType().FullName }); }
            }
            if (samples.Properties().All(p => ((JArray)p.Value).Count >= 50)) { break; }
        }
        report["functionsExamined"] = examined;
        report["elapsedMs"] = watch.ElapsedMilliseconds;
    }
}
