using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.Tests;

internal sealed partial class SmallProjectRedrawDriver
{
    private async Task PlaceDeviceWithPartAsync(string page, JObject item, JObject[] sourceObjects)
    {
        JObject[] parts = Objects(item["parts"]);
        // An earlier page already placed another representation of this device: join it, then assign the part.
        if ((bool?)item["isMain"] != true || parts.Length == 0 || IsPlaced(DeviceName(Text(item["name"]) ?? "")))
        {
            await PlaceDeviceAsync(page, item);
            return;
        }

        string name = Text(item["name"]) ?? throw new InvalidDataException("Exported function name is unreadable.");
        double[] location = Point(item["location"]);
        JObject part = parts[0];
        // Retain the pre-insertion identities so only this part's new objects count as coverage.
        JObject[] before = await ReadDrawingsAsync(page, item);
        bool beforeComplete = _lastDrawingReadComplete;
        if (!beforeComplete)
        {
            report.Warning(page, Type(item) + " " + Id(item) + ": part placement coverage cannot be established from an incomplete pre-insertion read.");
        }
        var prior = before.Select(DrawingKey).ToHashSet(StringComparer.Ordinal);
        JObject? rejection = null;
        JObject? placed = await InvokeAsync(page, "eplan_device_place", new JObject
        {
            ["page"] = page,
            ["part"] = part["part"]?.DeepClone(),
            ["variant"] = part["variant"]?.DeepClone(),
            ["tag"] = DeviceName(name),
            ["x"] = location[0],
            ["y"] = location[1],
        }, [item], onFailure: result => rejection = result);
        if (placed is null)
        {
            string? execution = Text(rejection?["error"]?["execution"] ?? rejection?["execution"]);
            if (execution is not ("not_started" or "rolled_back"))
            {
                report.Warning(page, Type(item) + " " + Id(item) + ": part placement outcome is unknown; no symbol fallback was replayed.");
                return;
            }
            report.Note(page, Type(item), Id(item), "Part route was rejected before commit; using the exported library/symbol fallback and assigning its part.");
            await PlaceDeviceAsync(page, item);
            return;
        }

        string actualName = Text(placed["device"]?["tag"]) ?? DeviceName(name);
        _actualNames[DeviceName(name)] = actualName;
        _placedTags.Add(actualName);
        report.RecordOrigin(page, item, "part");
        foreach (JObject additional in parts.Skip(1))
        {
            await InvokeAsync(page, "eplan_device_update", new JObject
            {
                ["tag"] = actualName,
                ["part"] = additional["part"]?.DeepClone(),
                ["variant"] = additional["variant"]?.DeepClone(),
            }, [item]);
        }
        foreach (JObject reference in parts.Where(reference => Number(reference["count"]) is double count && count != 1))
        { report.Note(page, Type(item), Id(item), "Part reference quantity " + reference["count"] + " cannot be specified by device_place/device_update."); }

        JObject[] after = await ReadDrawingsAsync(page, item);
        if (!_lastDrawingReadComplete)
        {
            report.Warning(page, Type(item) + " " + Id(item) + ": committed part placement has incomplete readback; symbol and coverage remain unverified.");
            return;
        }
        JObject[] candidates = after.Where(drawing => Text(drawing["properties"]?["name"]) == actualName
            && ClosePoint(item["location"], drawing["location"])).ToArray();
        bool? symbolMatches = candidates.Length == 1 ? await PartSymbolMatchesAsync(page, item, candidates[0]) : null;
        JObject[] added = beforeComplete ? after.Where(drawing => !prior.Contains(DrawingKey(drawing))).ToArray() : [];
        // The sample's parts have no macro boxes around them.
        JObject[] boxes = added.Where(drawing => Text(drawing["kind"]) == "MacroBox").ToArray();
        if (boxes.Length > 0)
        {
            await InvokeAsync(page, "eplan_placement_delete", new JObject { ["page"] = page, ["items"] = new JArray(boxes.Select(box => box["locator"]!.DeepClone())) }, [item]);
        }
        foreach (JObject source in sourceObjects.Where(source => !_covered.Contains(Key(page, source))))
        {
            JObject? match = added.FirstOrDefault(drawing => IsDevice(source) ? DrawingMatches(source, drawing)
                : IsGraphic(source) && GraphicMatches(source, drawing, compareStyle: false));
            if (match is null)
            { continue; }
            if (IsDevice(source) && (ReferenceEquals(source, item) ? symbolMatches : await PartSymbolMatchesAsync(page, source, match)) != true)
            { continue; }
            _covered.Add(Key(page, source));
            report.RecordOrigin(page, source, "part");
            added = added.Where(drawing => !ReferenceEquals(drawing, match)).ToArray();
        }
        if (_covered.Contains(Key(page, item)))
        { return; }

        if (symbolMatches == false)
        {
            report.Note(page, Type(item), Id(item), "Part route committed a different symbol; using the exported symbol fallback. "
                + "The committed part symbol cannot be removed with placement_delete and remains visible in the comparison.");
            await PlaceDeviceAsync(page, item, assignParts: false);
        }
        else
        { report.Warning(page, Type(item) + " " + Id(item) + ": part route committed, but the source symbol has no unambiguous geometry readback; no fallback was guessed."); }
    }

    private async Task<bool?> PartSymbolMatchesAsync(string page, JObject source, JObject drawing)
    {
        // page_get drawing locators deliberately expose geometry, not native SymbolReference data.
        // 20575 is the exported native symbol reference (library;variant;number;...).
        string? expected = Property(source, 20575);
        if (expected is null || drawing["locator"] is not JObject locator)
        { return null; }
        var address = new JObject
        {
            ["kind"] = "function",
            ["project"] = locator["project"]?.DeepClone(),
            ["type"] = locator["type"]?.DeepClone(),
            ["oid"] = locator["oid"]?.DeepClone(),
            ["page"] = page,
        };
        JObject? read = await InvokeAsync(page, "eplan_property_get", new JObject
        {
            ["queries"] = new JArray(new JObject
            {
                ["target"] = new JObject { ["locator"] = address },
                ["properties"] = new JArray(new JObject { ["property_id"] = 20575 }),
            }),
            ["limit"] = 1,
        }, [source], write: false);
        JObject[] values = Objects(read?["items"]).Where(value => (int?)value["property_id"] == 20575 && Text(value["status"]) == "value").ToArray();
        return values.Length == 1 && Text(values[0]["value"]) is string actual ? expected == actual : null;
    }
}
