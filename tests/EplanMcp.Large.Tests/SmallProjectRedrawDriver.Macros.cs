using System.Globalization;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.Tests;

internal sealed partial class SmallProjectRedrawDriver
{
    private readonly HashSet<string> _macroOwned = new(StringComparer.Ordinal);
    private readonly HashSet<string> _macroPages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JObject> _macroDescriptions = new(StringComparer.OrdinalIgnoreCase);

    private async Task InsertMacrosAsync(string page, JObject[] objects, string pageType, IReadOnlyList<JObject> allPages)
    {
        var instances = objects.Where(item => !string.IsNullOrWhiteSpace(Property(item, 20468)))
            .GroupBy(item => (Macro: Property(item, 20468)!.Replace('\\', '/'), Tag: DeviceName(Text(item["name"]) ?? "")))
            .Select(group => (group.Key.Macro, group.Key.Tag, Members: group.ToArray())).ToList();
        foreach (JObject box in objects.Where(item => string.IsNullOrWhiteSpace(Property(item, 20468))
            && (pageType == "Overview" && Text(item["category"]) == "PLCBox" || pageType == "Circuit" && Type(item) == "BoxedDevice" && Objects(item["parts"]).Length > 0)))
        {
            string tag = Text(box["name"]) ?? "";
            if (!instances.Any(instance => instance.Tag == tag))
            { instances.Add(("", tag, [box])); }
        }
        foreach (var instance in instances)
        {
            JObject[] members = instance.Members.Concat(objects.Where(item => IsDevice(item)
                && DeviceName(Text(item["name"]) ?? "") == instance.Tag && string.IsNullOrWhiteSpace(Property(item, 20468))))
                .Distinct().ToArray();
            JObject? owner = members.Concat(allPages.SelectMany(source => Objects(source["placements"])))
                .FirstOrDefault(item => Text(item["name"]) == instance.Tag && Objects(item["parts"]).Length > 0);
            JObject? part = owner is null ? null : Objects(owner["parts"]).FirstOrDefault();
            bool partRoute = instance.Macro.Length == 0 || pageType == "Overview" && members.Any(item => Text(item["category"]) == "PLCBox"
                && string.IsNullOrWhiteSpace(Property(item, 20468)));
            await ObserveAsync(page, members.FirstOrDefault() ?? instance.Members[0], async () =>
            {
                string requested = instance.Macro;
                string? path = requested.Length == 0 ? null : await FindMacroAsync(page, instance.Members[0], requested);
                bool replacement = requested.Equals("PXC/2703981.ema", StringComparison.OrdinalIgnoreCase) && path is null;
                if (partRoute || replacement)
                {
                    if (part is null || string.IsNullOrWhiteSpace(Text(part["part"])))
                    {
                        report.Warning(page, "Macro instance " + instance.Tag + ": the part route has no readable source part reference.");
                        return;
                    }
                    path = await FindPartMacroAsync(page, members.FirstOrDefault() ?? instance.Members[0], part, pageType, allPages);
                }
                if (path is null)
                {
                    report.Note(page, Type(instance.Members[0]), Id(instance.Members[0]), "Macro " + requested + " is unavailable; the ordinary device route will handle its functions.");
                    return;
                }
                string representation = pageType == "Overview" ? "Overview" : "MultiLine";
                JObject? description = await DescribeMacroAsync(page, instance.Members[0], path, representation, 0);
                if (description is null && !partRoute && !replacement
                    && requested.Equals("PXC/2703981.ema", StringComparison.OrdinalIgnoreCase)
                    && part is not null)
                {
                    replacement = true;
                    path = await FindPartMacroAsync(page, members.FirstOrDefault() ?? instance.Members[0], part, pageType, allPages);
                    if (path is not null)
                    { description = await DescribeMacroAsync(page, instance.Members[0], path, representation, 0); }
                }
                if (description is null || path is null)
                { return; }
                if (!Objects(description["representations"]).Any(item => Text(item["type"]) == representation
                    && (item["variants"] as JArray)?.Any(variant => (int?)variant == 0) == true))
                {
                    report.Warning(page, "Macro " + path + ": required " + representation + " variant 0 is unavailable; no insertion was guessed.");
                    return;
                }
                JObject anchor = members.FirstOrDefault(item => (bool?)item["isMain"] == true)
                    ?? UniqueMembers(members).FirstOrDefault() ?? instance.Members[0];
                bool module = members.All(item => Text(item["category"]) == "PLCTerminal");
                int variant = 0, votes = 0;
                double[]? best = null;
                JObject chosen = description;
                foreach (int candidate in Objects(description["representations"]).Where(item => Text(item["type"]) == representation)
                    .SelectMany(item => (item["variants"] as JArray)?.Select(value => (int?)value ?? 0) ?? []).Distinct().OrderBy(value => value))
                {
                    JObject? described = candidate == 0 ? description : await DescribeMacroAsync(page, instance.Members[0], path, representation, candidate);
                    if (described is null)
                    { continue; }
                    (double[]? offset, int score) = BestTranslation(members, Objects(described["devices"]));
                    if (score > votes)
                    {
                        (chosen, variant, best, votes) = (described, candidate, offset, score);
                    }
                }
                if (best is not { } translation || !TryPoint(chosen["reference_point"], out double[] reference))
                {
                    report.Warning(page, "Macro " + path + ": reference_point or anchor location is unreadable; no insertion position was guessed.");
                    return;
                }
                CheckDefaultVariant(page, path, members, Objects(chosen["devices"]), translation);
                JObject[] before = await ReadDrawingsAsync(page, anchor);
                if (!_lastDrawingReadComplete)
                {
                    report.Warning(page, "Macro " + path + ": complete pre-insertion drawing evidence is unavailable; no insertion was attempted.");
                    return;
                }
                var args = new JObject
                {
                    ["page"] = page,
                    ["representation"] = representation,
                    ["variant"] = variant,
                    ["x"] = reference[0] + translation[0],
                    ["y"] = reference[1] + translation[1],
                    ["macro_box"] = false,
                };
                // A module's points get the card's tag next; numbering them first would only use up free numbers.
                if (module)
                { args["numbering"] = "keep"; }
                if (partRoute)
                {
                    args["part"] = part!["part"]!.DeepClone();
                    args["part_variant"] = part["variant"]?.DeepClone();
                }
                else
                { args["macro"] = path; }
                JObject? result = await InvokeAsync(page, "eplan_macro_insert", args, members);
                if (result is null)
                {
                    report.Note(page, Type(anchor), Id(anchor), "Macro insertion failed; the ordinary device and graphic fallback routes remain available.");
                    return;
                }
                _macroPages.Add(page);
                report.Note(page, Type(anchor), Id(anchor), "Macro instance " + instance.Tag + " used " + (Text(result["macro"]) ?? path)
                    + " at (" + args["x"] + ", " + args["y"] + "), " + representation + " variant " + variant + (partRoute ? " through its source part." : "."));
                if ((bool?)result["recovered"] == true)
                { report.Note(page, Type(anchor), Id(anchor), "Recovered macro response contains template tags; fresh page drawings determine coverage without renaming those placeholders."); }
                else
                { await UpdateMacroDeviceAsync(page, anchor, instance.Tag, partRoute || module ? null : part, result); }
                JObject[] after = await ReadDrawingsAsync(page, anchor);
                if (!_lastDrawingReadComplete)
                {
                    foreach (JObject member in members)
                    {
                        _macroOwned.Add(Key(page, member));
                        report.NoTool(page, Type(member), Id(member), "Macro insertion committed, but coverage readback is incomplete; no duplicate function was placed.");
                    }
                    return;
                }
                HashSet<string> existing = before.Select(DrawingKey).ToHashSet(StringComparer.Ordinal);
                JObject[] added = after.Where(item => !existing.Contains(DrawingKey(item))).ToArray();
                MarkMacroCoverage(page, objects, members, added, requested, Text(result["macro"]) ?? path, replacement, pageType == "Overview");
            });
        }
    }

    private async Task<string?> FindPartMacroAsync(string page, JObject evidence, JObject part, string pageType, IReadOnlyList<JObject> allPages)
    {
        JObject? sourcePage = allPages.FirstOrDefault(item => Text(item["name"]) == page);
        double[] preview = TryPoint(evidence["location"], out double[] anchor) ? anchor : [];
        if (Number(sourcePage?["size"]?["y"]) is double height && height > 0)
        { preview = [0, height]; }
        if (preview.Length != 2)
        {
            report.Warning(page, "Part macro preview has neither readable page height nor anchor coordinates; no preview position was guessed.");
            return null;
        }
        var args = new JObject
        {
            ["page"] = page,
            ["part"] = part["part"]?.DeepClone(),
            ["part_variant"] = part["variant"]?.DeepClone(),
            ["representation"] = pageType == "Overview" ? "Overview" : "MultiLine",
            ["variant"] = 0,
            ["x"] = preview[0],
            ["y"] = preview[1],
            ["macro_box"] = false,
            ["dry_run"] = true,
        };
        JObject? result = await InvokeAsync(page, "eplan_macro_insert", args, [evidence], write: false);
        string? path = Text(result?["macro"])?.Replace('\\', '/');
        if (path is null)
        { report.Warning(page, "Part " + Text(part["part"]) + ": preview did not expose its library macro path; the ordinary part route remains available."); }
        return path;
    }

    private async Task<string?> FindMacroAsync(string page, JObject evidence, string wanted)
    {
        string file = wanted.Split('/')[^1];
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            var args = new JObject { ["name_contains"] = file, ["limit"] = 200 };
            if (cursor is not null)
            { args["cursor"] = cursor; }
            JObject? result = await InvokeAsync(page, "eplan_macro_list", args, [evidence], write: false);
            if (result is null)
            { return null; }
            foreach (JObject item in Objects(result["macros"]))
            {
                string? path = Text(item["path"])?.Replace('\\', '/');
                if (path is not null && path.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                { paths.Add(path); }
            }
            cursor = Text(result["nextCursor"]);
            if (((bool?)result["truncated"] == true && cursor is null) || (cursor is not null && !cursors.Add(cursor)))
            {
                report.Warning(page, "Macro listing for " + wanted + " is incomplete; no library path was guessed.");
                return null;
            }
        } while (cursor is not null);
        return paths.Count == 1 ? paths.Single() : null;
    }

    private async Task<JObject?> DescribeMacroAsync(string page, JObject evidence, string path, string representation, int variant)
    {
        string key = path + "\n" + representation + "\n" + variant;
        if (_macroDescriptions.TryGetValue(key, out JObject? cached))
        { return cached; }
        JObject? result = await InvokeAsync(page, "eplan_macro_get", new JObject { ["macro"] = path, ["representation"] = representation, ["variant"] = variant }, [evidence], write: false);
        if (result?["macro"] is not JObject description)
        {
            report.Warning(page, "Macro " + path + ": metadata is unreadable; the ordinary device route remains available.");
            return null;
        }
        _macroDescriptions[key] = description;
        return description;
    }

    // The position most members agree on: the sample's engineer moved single points (=K1+A2-KEB1:X8) after inserting.
    private static (double[]? Translation, int Votes) BestTranslation(JObject[] members, JObject[] devices)
    {
        var votes = new Dictionary<string, (double[] Translation, int Count)>(StringComparer.Ordinal);
        foreach (JObject member in UniqueMembers(members).OrderByDescending(item => (bool?)item["isMain"] == true))
        {
            JObject? device = devices.FirstOrDefault(item => Text(item["category"]) == Text(member["category"])
                && FunctionSuffix(Text(item["tag"])) == FunctionSuffix(Text(member["name"])));
            if (device is null || !TryPoint(device["location"], out double[] location) || !TryPoint(member["location"], out double[] source))
            { continue; }
            double[] translation = [source[0] - location[0], source[1] - location[1]];
            string key = string.Format(CultureInfo.InvariantCulture, "{0:0.00},{1:0.00}", translation[0], translation[1]);
            votes[key] = (translation, votes.TryGetValue(key, out (double[] Translation, int Count) seen) ? seen.Count + 1 : 1);
        }
        return votes.Count == 0 ? (null, 0) : votes.Values.MaxBy(vote => vote.Count);
    }

    private static string FunctionSuffix(string? name) => name is not null && name.Contains(':', StringComparison.Ordinal)
        ? name[name.IndexOf(':', StringComparison.Ordinal)..] : "";

    // A macro lists one row per tag, so a point name that repeats (RJ45 on two plugs) cannot be compared by position.
    private static IEnumerable<JObject> UniqueMembers(JObject[] members) => members
        .GroupBy(member => Text(member["name"]) ?? "", StringComparer.Ordinal).Where(group => group.Count() == 1).Select(group => group.Single());

    private static bool MembersMatch(JObject[] members, JObject[] devices, double[] translation) =>
        UniqueMembers(members).All(member => devices.Any(device => Text(device["category"]) == Text(member["category"])
            && FunctionSuffix(Text(device["tag"])) == FunctionSuffix(Text(member["name"]))
            && TryPoint(device["location"], out double[] location) && TryPoint(member["location"], out double[] source)
            && Near(source[0], location[0] + translation[0]) && Near(source[1], location[1] + translation[1])));

    private void CheckDefaultVariant(string page, string path, JObject[] members, JObject[] devices, double[] translation)
    {
        if (!MembersMatch(members, devices, translation))
        { report.Warning(page, "Macro " + path + ": the readable default-variant function positions do not all match. Per-variant geometry is not exposed by the agreed macro_get request; using variant 0 and retaining every readback difference."); }
    }

    private async Task UpdateMacroDeviceAsync(string page, JObject anchor, string wanted, JObject? part, JObject result)
    {
        string[] tags = Objects(result["devices"]).Select(item => Text(item["tag"]) ?? "").Where(tag => tag.Length > 0).ToArray();
        string[] roots = tags.Where(tag => FunctionSuffix(tag) == "").ToArray();
        // A PLC module macro holds points only: their common prefix is the tag that joins the card.
        if (roots.Length == 0)
        { roots = tags.Select(DeviceName).Distinct(StringComparer.Ordinal).ToArray(); }
        if (roots.Length != 1)
        {
            report.Warning(page, "Macro instance " + wanted + ": the inserted root tag is absent or ambiguous; no rename was guessed.");
            return;
        }
        string inserted = roots[0];
        if (inserted != wanted && await InvokeAsync(page, "eplan_device_update", new JObject { ["tag"] = inserted, ["new_tag"] = wanted }, [anchor]) is null)
        { return; }
        _placedTags.Add(wanted);
        _actualNames[wanted] = wanted;
        if (part is null)
        { return; }
        JObject? device = await InvokeAsync(page, "eplan_device_get", new JObject { ["tag"] = wanted }, [anchor], write: false);
        if (!Objects(device?["parts"]).Any(item => Text(item["partNumber"]) == Text(part["part"])))
        {
            await InvokeAsync(page, "eplan_device_update", new JObject { ["tag"] = wanted, ["part"] = part["part"]?.DeepClone(), ["variant"] = part["variant"]?.DeepClone() }, [anchor]);
        }
    }

    private void MarkMacroCoverage(string page, JObject[] objects, JObject[] members, JObject[] actual,
        string requested, string used, bool replacement, bool replaceText)
    {
        var available = actual.ToList();
        foreach (JObject source in objects.Where(item => IsDevice(item) || IsGraphic(item)
            || Type(item) is "LocationBox" or "SymbolReference" or "InterruptionPoint" or "ConnectionDefinitionPoint" or "PotentialDefinition" or "Cable"))
        {
            JObject? matched = available.FirstOrDefault(item => IsGraphic(source) ? GraphicMatches(source, item, compareStyle: false) : DrawingMatches(source, item));
            if (matched is not null)
            {
                available.Remove(matched);
                // Overview text is replaced with all source languages after the native macro is present.
                if (!replaceText || Type(source) is not ("Text" or "PathText"))
                { _covered.Add(Key(page, source)); }
                report.RecordOrigin(page, source, "macro");
                report.Note(page, Type(source), Id(source), "Macro object retained after matching fresh geometry within 0.01 mm.");
            }
            else if (members.Contains(source))
            {
                _macroOwned.Add(Key(page, source));
                if (replacement)
                { report.RecordMacroVersionDifference(page, source, requested, used, "The replacement macro does not reproduce this source function's geometry or symbol; no individual repair was attempted."); }
                else
                { report.NoTool(page, Type(source), Id(source), "The inserted macro does not reproduce this source function's geometry or symbol; no duplicate was placed."); }
            }
        }
    }

    private async Task ReplaceOverviewMacroTextAsync(string page, JObject[] objects)
    {
        JObject evidence = objects.FirstOrDefault() ?? new JObject { ["type"] = "Page" };
        JObject[] actual = await ReadDrawingsAsync(page, evidence);
        if (!_lastDrawingReadComplete)
        {
            report.Warning(page, "Overview text replacement requires complete drawing evidence; no text deletion or duplicate was attempted.");
            return;
        }
        JObject[] sourceTexts = objects.Where(item => Type(item) is "Text" or "PathText").ToArray();
        JObject[] existingTexts = actual.Where(item => IsUngroupedDrawing(item) && Text(item["kind"]) is "Text" or "PathText").ToArray();
        foreach (JObject[] batch in existingTexts.Chunk(100))
        {
            if (await InvokeAsync(page, "eplan_placement_delete", new JObject
            {
                ["page"] = page,
                ["items"] = new JArray(batch.Select(item => item["locator"]!.DeepClone())),
            }, batch.Select(item => new JObject { ["type"] = item["kind"]?.DeepClone(), ["oid"] = item["locator"]?["oid"]?.DeepClone() }).ToArray()) is null)
            {
                report.Warning(page, "Overview macro text deletion failed; source text was not duplicated.");
                return;
            }
        }
        await DrawGraphicsAsync(page, sourceTexts);
        foreach (JObject source in objects.Where(IsGraphic).Where(item => Type(item) is not ("Text" or "PathText") && !_covered.Contains(Key(page, item))))
        { report.NoTool(page, Type(source), Id(source), "Overview macro graphic is retained as inserted. Source geometry has no exact match; no graphic repair or duplicate was attempted."); }
    }
}
