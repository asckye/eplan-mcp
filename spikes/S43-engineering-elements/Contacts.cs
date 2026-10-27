using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S43;

internal sealed partial class Probe
{
    private static readonly int[] _designationProperties = [20075, 20076, 20226, 20227];
    private JObject ContactState(Function function)
    {
        var row = new ReadRecord();
        Pin[]? pins = row.Get<Pin[]?>("sourcePins", () => function.Pins);
        int count = pins?.Length ?? 0;
        row.Field("state", () => ObjectState(function));
        row.Field("all20038", () => Value(function, 20038));
        row.Array("indexed20022", () => pins is null ? null : Enumerable.Range(1, count), i => Value(function, 20022, i));
        row.Array("computed20028", () => pins is null ? null : Enumerable.Range(1, count), i => Value(function, 20028, i));
        row.Array("fixedProperties", () => _designationProperties.Take(count), id => new JObject {
            ["property"] = id, ["value"] = Diagnose(() => Value(function, id)) });
        row.Array("pins", () => pins, pin => PinData(pin, function));
        return row.Data;
    }
    private void SetConnectionDesignations(Function function, string[] requested, Action<string, object> log)
    {
        var errors = new List<Exception>();
        // Installed XML: Pin.Designation is internal and read-only; Pin.Name is the actual 20022 value.
        // 20022 is indexed from 1. The fixed aliases are documented read-only; probe them only as a fallback.
        foreach (string variant in new[] { "indexed20022", "fixed20075_20076_20226_20227" })
        {
            if (variant != "indexed20022" && requested.Length > _designationProperties.Length) { continue; }
            TraceStage("contact.designations.set", new { variant, requested, before = ContactState(function),
                pinComparison = "Pin.Name (actual designation); Pin.Designation remains internal", fixedAliasesDocumentedReadOnly = true });
            for (int i = 0; i < requested.Length; i++)
            {
                int property = variant == "indexed20022" ? 20022 : _designationProperties[i];
                int index = variant == "indexed20022" ? i + 1 : 0;
                try { Set(function, property, requested[i], index); }
                catch (Exception ex) when (ex is BaseException or ArgumentException or InvalidOperationException)
                {
                    errors.Add(ex);
                    log("contact_designation_set_error", new { variant, property, index, requested = requested[i], error = Evidence.Error(ex) });
                }
            }
            JObject state = ContactState(function);
            JArray? pins = state["pins"] as JArray;
            string?[]? actual = pins?.Select(p => p is JObject item ? (string?)item["name"] : null).ToArray();
            string?[]? internalDesignations = pins?.Select(p => p is JObject item ? (string?)item["designation"] : null).ToArray();
            bool exact = actual is not null && actual.SequenceEqual(requested, StringComparer.Ordinal) &&
                state["indexed20022"] is JArray input && input.Count == requested.Length &&
                input.Select(p => p.Type == JTokenType.String ? (string?)p : null).SequenceEqual(requested, StringComparer.Ordinal);
            log("contact_designation_readback", new { variant, requested, actual, internalDesignations, exact,
                workedVariant = exact ? variant : null, acceptanceField = "Pin.Name + indexed 20022", state });
            if (exact) { return; }
        }
        log("contact_designation_failed", new { requested, state = ContactState(function) });
        throw new InvalidOperationException("Actual connection point designations differ; inspect Pin.Name, internal Pin.Designation and property variants.",
            errors.Count == 0 ? null : new AggregateException(errors));
    }
}
