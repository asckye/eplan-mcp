using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Operations;

/// <summary>The set_main_function operation of eplan_placement_edit: makes placed functions the device's main function, or not.</summary>
internal static class PlacementMainFunction
{
    public const string Operation = "set_main_function";
    public const string Field = "main_function";
    public const string ClearedWarning = "A device without a main function leaves the device and parts lists, and its parts cannot be assigned until a function is main again.";

    /// <summary>Plans one function. A device has one main function, so making a second one main is refused.</summary>
    public static void Predict(JObject row, bool main, IReadOnlyDictionary<string, int>? mains, JArray planned)
    {
        if (row["mainFunction"]?.Type != JTokenType.Boolean)
        { throw WriteArguments.Invalid(["Select placed functions: the object is no function."]); }
        string name = (string?)row["functionName"] ?? string.Empty;
        if (main && !(bool)row["mainFunction"]!)
        {
            if (mains is not null && mains.TryGetValue(name, out int count) && count > 0)
            { throw WriteArguments.Invalid(["'" + name + "' already has a main function; make that one non-main first."]); }
            if (planned.Any(p => (string?)p["functionName"] == name))
            { throw WriteArguments.Invalid(["Make only one function of '" + name + "' main."]); }
        }
        row["mainFunction"] = main;
    }
}
