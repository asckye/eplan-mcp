using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Topology;
using Newtonsoft.Json.Linq;
using Formatting = Newtonsoft.Json.Formatting;

namespace EplanMcp.Spikes.S26;

public sealed partial class SpikeS26Action
{
    private static JObject R2Readback(Project project)
    {
        using var finder = new DMObjectsFinder(project);
        var state = new JObject();
        Capture(state, "projectType", () => new JValue(project.TypeOfProject.ToString()));
        Capture(state, "pages", () => new JArray(finder.GetPages(null).OrderBy(page => page.ObjectIdentifier).Select(page =>
            new JObject { ["id"] = page.ObjectIdentifier, ["name"] = page.Name, ["type"] = page.PageType.ToString() })));
        Capture(state, "functions", () =>
        {
            var rows = new JArray();
            foreach (var function in finder.GetFunctions(null).OrderBy(function => function.ObjectIdentifier))
            {
                var row = new JObject { ["id"] = function.ObjectIdentifier };
                rows.Add(row);
                Capture(row, "name", () => new JValue(function.Name));
                Capture(row, "page", () => new JValue(function.Page?.Name));
                Capture(row, "text", () =>
                {
                    // Acquire the property list once, then dispose only its values and the list (A36/A39).
                    using var properties = function.Properties;
                    using var value = properties.FUNC_TEXT;
                    return value.IsEmpty ? JValue.CreateNull() : new JValue(value.ToString());
                });
            }
            return rows;
        });
        Capture(state, "macroBoxes", () => new JArray(finder.GetAll<MacroBox>(true).Cast<MacroBox>().OrderBy(box => box.ObjectIdentifier).Select(box =>
            new JObject { ["id"] = box.ObjectIdentifier, ["name"] = box.MacroName, ["group"] = box.Group?.ObjectIdentifier,
                ["handleActive"] = box.InsertionPointActive, ["protectedGroup"] = box.GenerateProtectedGroup })));
#if S26_R3
        Capture(state, "groups", () => R3Groups(finder));
#else
        Capture(state, "groups", () => new JArray(finder.GetAll<Group>(true).Cast<Group>().OrderBy(group => group.ObjectIdentifier).Select(group =>
            new JObject { ["id"] = group.ObjectIdentifier, ["members"] = new JArray(group.SubPlacements.Select(item => item.ObjectIdentifier).OrderBy(id => id)) })));
#endif
        Capture(state, "segments", () =>
        {
            var rows = new JArray();
            foreach (var segment in finder.GetAll<Segment>(true).Cast<Segment>().OrderBy(segment => segment.ObjectIdentifier))
            {
                var row = new JObject { ["id"] = segment.ObjectIdentifier };
                rows.Add(row);
                Capture(row, "fill", () =>
                {
                    using var properties = segment.Properties;
                    using var value = properties.CABLINGSEGMENT_FILLING;
                    return value.IsEmpty ? JValue.CreateNull() : new JValue(value.ToString());
                });
            }
            return rows;
        });
        return state;
    }

    private static string R2Digest(JToken value)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value.ToString(Formatting.None)))).Replace("-", string.Empty);
    }

    private static void R2Atomic(string path, JObject value)
    {
        string full = SpikeS26Paths.Below(path, SpikeS26Paths.OutputRoot);
        SpikeS26Paths.Below(full, SpikeS26Paths.Scratch);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full + ".partial", value.ToString(Formatting.None), new UTF8Encoding(false));
        File.Move(full + ".partial", full); // Evidence files are immutable and never reused.
    }

    private static void R2Capture(JObject result, string stage, Candidate c, Parameters p, string[] paths)
    {
        if (c.SourceRow == 0) { return; }
        var summaries = new JObject();
        result["r2Readbacks"] ??= new JObject();
        result["r2Readbacks"]![stage] = summaries;
        Capture(summaries, "snapshot", () =>
        {
            var projects = new JObject();
            var fileManifests = new JObject();
            using var manager = new ProjectManager();
            foreach (string path in paths)
            {
                JObject state = R2Readback(FindProject(manager, path));
                projects[path] = state;
                string[] fields = c.SourceRow switch { 117 => ["projectType"], 76 => ["macroBoxes", "groups"], 133 => ["segments"], _ => ["functions"] };
                var effect = new JObject(fields.Select(field => new JProperty(field, state[field]!.DeepClone())));
                if (c.SourceRow is >= 118 and <= 120 && state["functions"] is JArray functions)
                { effect = new JObject(functions.OfType<JObject>().Select(function => new JProperty(function["id"]!.ToString(), function["text"]!.DeepClone()))); }
                summaries[path] = new JObject { ["semanticSha256"] = R2Digest(state), ["effectSha256"] = R2Digest(effect),
                    ["readErrors"] = state.Descendants().OfType<JProperty>().Count(property => property.Name == "error"), ["projectType"] = state["projectType"]!.DeepClone() };
#if S26_R3
                summaries[path]!["readFailures"] = new JArray(state.Descendants().OfType<JProperty>().Where(property => property.Name == "error")
                    .Select(property => new JObject { ["field"] = property.Parent!.Path, ["exception"] = property.Value.DeepClone() }));
                summaries[path]!["knownGaps"] = new JArray(state.Descendants().OfType<JProperty>().Where(property => property.Name == "knownGap")
                    .Select(property => new JObject { ["field"] = property.Parent!.Path, ["reason"] = property.Value.DeepClone() }));
#endif
                FileSnapshot files = ProjectFiles(path); // Complete hashes, including User.*.
                JObject link = FileStamp(path);
                files.Entries["../" + Path.GetFileName(path)] = link;
                fileManifests[path] = JObject.FromObject(files.Entries);
                summaries[path]!["link"] = link.DeepClone();
                summaries[path]!["manifestHashErrors"] = files.Entries.Values.Count(stamp => stamp["error"] is not null);
                summaries[path]!["manifestUnstableFiles"] = files.Entries.Values.Count(stamp => stamp.Value<bool?>("changedDuringHash") == true);
                var settings = new JObject(files.Entries.Where(pair => string.Equals(Path.GetFileName(pair.Key), "Settings.xdb", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new JProperty(pair.Key, pair.Value["sha256"]?.DeepClone())));
                summaries[path]!["settingsSha256"] = R2Digest(settings);
            }
            var snapshot = new JObject { ["stage"] = stage, ["projects"] = projects, ["projectFiles"] = fileManifests };
            if (c.SourceRow == 155)
            {
                using var scheme = new SchemeSetting();
                scheme.Init(p.CommonSchemeNode);
                summaries["lastUsedScheme"] = scheme.GetLastUsed();
                snapshot["lastUsedScheme"] = summaries["lastUsedScheme"]?.DeepClone();
            }
            string file = Path.Combine(p.Folder, c.Id + "-evidence", stage + ".json");
            R2Atomic(file, snapshot);
            return new JObject { ["path"] = file, ["stamp"] = FileStamp(file), ["truncated"] = false };
        });
    }

    private static void R2RequireReadable(JObject result, string[] paths)
    {
        if (result["r2Readbacks"] is null) { return; }
#if S26_R3
        foreach (string path in paths)
        {
            JObject? state = result["r2Readbacks"]?["before"]?[path] as JObject;
            if (state?.Value<int?>("readErrors") != 0)
            { throw new InvalidOperationException("Baseline read failed: " + path + "; " + state?["readFailures"]?.ToString(Formatting.None)); }
            if (result.Value<int?>("sourceRow") == 76 && state?["knownGaps"] is JArray gaps && gaps.Count > 0)
            { throw new InvalidOperationException("Macro group verification requires complete group membership: " + path + "; " + gaps.ToString(Formatting.None)); }
        }
#endif
        if (result["r2Readbacks"]?["before"]?["snapshot"]?["error"] is not null || paths.Any(path =>
            result["r2Readbacks"]?["before"]?[path]?.Value<int?>("readErrors") != 0 ||
            result["r2Readbacks"]?["before"]?[path]?.Value<int?>("manifestHashErrors") != 0 ||
            result["r2Readbacks"]?["before"]?[path]?.Value<int?>("manifestUnstableFiles") != 0 ||
            HasError(result["before"]?["projects"]?[path] ?? new JObject { ["error"] = "Missing stage" }) ||
            result["before"]?["projects"]?[path]?["files"]?.Value<int?>("hashErrors") != 0 ||
            result["before"]?["projects"]?[path]?["files"]?.Value<int?>("unstableFiles") != 0))
        { throw new InvalidOperationException("Incomplete or unstable baseline; refusing action."); }
    }

    private static JObject R2Output(Candidate c, Parameters p)
    {
        string[] files = Directory.Exists(c.Folder) ? SafeFiles(c.Folder).OrderBy(path => path, StringComparer.Ordinal).ToArray() : [];
        var rows = new JArray(files.Select(path => new JObject { ["path"] = path, ["relativePath"] = path.Substring(c.Folder.Length + 1), ["stamp"] = FileStamp(path) }));
        string manifest = Path.Combine(p.Folder, c.Id + "-evidence", "output-manifest.json");
        R2Atomic(manifest, new JObject { ["files"] = rows, ["truncated"] = false });
        var output = new JObject { ["manifest"] = manifest, ["stamp"] = FileStamp(manifest), ["count"] = files.Length };
        if (c.SourceRow is not (75 or 155)) { output["valid"] = files.Length == 0; return output; }
        string requested = c.Options[c.SourceRow == 75 ? "DESTINATIONFILE" : "DESTINATION"];
        output["expectedPath"] = requested;
        output["exactProducedPath"] = files.Length == 1 && string.Equals(files[0], requested, StringComparison.Ordinal);
        bool contains = false;
        if (output.Value<bool>("exactProducedPath") && new FileInfo(requested).Length is > 0 and <= 64 * 1024 * 1024)
        {
            if (c.SourceRow == 155)
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 64 * 1024 * 1024 };
                using var reader = XmlReader.Create(requested, settings);
                while (reader.Read())
                {
                    if (reader.HasValue && reader.Value == c.ExpectedToken) { contains = true; }
                    if (reader.MoveToFirstAttribute())
                    {
                        do { if (reader.Value == c.ExpectedToken) { contains = true; } } while (reader.MoveToNextAttribute());
                        reader.MoveToElement();
                    }
                }
            }
            else
            {
                using var reader = new StreamReader(requested, detectEncodingFromByteOrderMarks: true);
                string? line;
                while ((line = reader.ReadLine()) is not null)
                { contains |= line.Split(';', ',', '\t').Any(field => field.Trim().Trim('"') == c.ExpectedToken); }
            }
        }
        output["sampleTokenFound"] = contains;
        output["valid"] = contains && rows.OfType<JObject>().All(row => row["stamp"]?["error"] is null && row["stamp"]?.Value<bool?>("changedDuringHash") == false);
        output["contentCheckLimitation"] = "Exact sample value and syntax only; engineer must review the complete exported data. No exported content is copied into the report.";
        return output;
    }

    private static void R2Finish(JObject result, Candidate c, Parameters p, string[] paths)
    {
        if (c.SourceRow == 0) { return; }
        result["sourceRow"] = c.SourceRow;
        Capture(result, "r2Output", () => R2Output(c, p));
#if S26_R3
        JObject assessment = SpikeS26R3Assessment.Assess(result, paths, p.Project, c.SourceRow is 75 or 155, c.ExpectedType);
#else
        JObject assessment = SpikeS26R2Assessment.Assess(result, paths, p.Project, c.SourceRow is 75 or 155, c.ExpectedType);
#endif
        result["r2Assessment"] = assessment;
        result["unsafeToContinue"] = result.Value<bool?>("unsafeToContinue") == true || assessment.Value<bool>("unsafeToContinue");
    }
}
