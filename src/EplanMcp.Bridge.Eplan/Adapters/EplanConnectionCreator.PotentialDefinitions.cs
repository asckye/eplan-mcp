using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanConnectionCreator
{
    private static readonly (string Name, int Id)[] _potentialProperties = [("potential_value", 33003), ("potential_type", 31006), ("color", 31004), ("cross_section", 31002)];

    private static PotentialDefinition InsertPotential(Page page, JToken row)
    {
        string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ema");
        try
        {
            using (var resource = typeof(EplanConnectionCreator).Assembly.GetManifestResourceStream("EplanMcp.Bridge.Eplan.Resources.potential-definition.ema")
                ?? throw new InvalidOperationException("Potential definition resource is missing."))
            using (var output = System.IO.File.Create(file))
            { resource.CopyTo(output); }
            using var insert = new Insert();
            StorableObject[] inserted = insert.WindowMacro(file, WindowMacro.Enums.RepresentationType.MultiLine, 0, page,
                new PointD((double)row["position"]![0]!, (double)row["position"]![1]!), Insert.MoveKind.Absolute, WindowMacro.Enums.NumerationMode.None);
            foreach (MacroBox box in inserted.OfType<MacroBox>())
            { box.Remove(); }
            PotentialDefinition point = inserted.OfType<PotentialDefinition>().Single();
            if (row["properties"]!["potential_name"] is { } potential)
            { point.PotentialName = (string)potential!; }
            if (row["properties"]!["signal_name"] is { } signal)
            { point.SignalName = (string)signal!; }
            using UniversalPropertyList properties = point.Properties;
            foreach (var property in _potentialProperties)
            {
                if (row["properties"]![property.Name] is not { } value)
                { continue; }
                using AnyPropertyId id = property.Id;
                if (property.Id == 31006)
                { properties[id] = (int)value; }
                else
                { properties[id] = (string)value!; }
            }
            return point;
        }
        finally { if (System.IO.File.Exists(file)) { System.IO.File.Delete(file); } }
    }

    private static JObject ReadPotential(PotentialDefinition point, JToken expected, PageDrawingGeometry geometry)
    {
        var values = new JObject { ["potential_name"] = point.PotentialName, ["signal_name"] = point.SignalName };
        using UniversalPropertyList properties = point.Properties;
        foreach (var property in _potentialProperties)
        {
            if (expected["properties"]![property.Name] is null)
            { continue; }
            using AnyPropertyId id = property.Id;
            using PropertyValue value = properties[id];
            values[property.Name] = property.Id == 31006 ? new JValue(value.ToInt()) : new JValue(value.ToString());
        }
        return new JObject
        {
            ["itemIndex"] = expected["itemIndex"]!.DeepClone(),
            ["type"] = point.GetType().Name,
            ["position"] = new JArray(XY(point.Location)),
            ["properties"] = values,
            ["locator"] = BridgeJson.FromObject(geometry.Placements.Single(p => p.Kind == nameof(PotentialDefinition) && p.Locator.Oid == point.ObjectIdentifier).Locator)
        };
    }
}
