using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Operations;

public interface IMacroStorer
{
    public bool KnowsLanguage(string language);
    public Func<int[]> Prepare(string projectPath, JObject args);
}

/// <summary>File export with read-only project observation; no write transaction or undo step.</summary>
public sealed class MacroStoreOperation(IProjectCatalog catalog, IMacroStorer storer, ISnapshotReader snapshots) : Operation<JObject, JObject>
{
    public override string Name => OperationNames.MacroStore;
    protected override JObject Execute(JObject args)
    {
        JObject input = (JObject)args.DeepClone();
        Require(input["operation"]?.Type == JTokenType.String, "operation must be a string.");
        bool window = (string?)input["operation"] == "window";
        Require(window || (string?)input["operation"] == "page", "operation must be window or page.");
        ConnectionCreatePlanner.Members(input, "", window ? "project operation page locators name path" : "project operation pages name path",
            "description dry_run fingerprint" + (window ? " representation variant reference_mm" : ""));
        ConnectionCreatePlanner.FiniteNumbers(input, "");
        Require(input["project"]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)input["project"]), "Specify an open project.");
        Require(input["dry_run"] is null || input["dry_run"]!.Type == JTokenType.Boolean, "dry_run must be boolean.");
        Require(input["name"]?.Type == JTokenType.String && input["path"]?.Type == JTokenType.String, "name and the Host output path must be strings.");
        string name = (string)input["name"]!;
        Require(name.Length > 0 && name.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-' or '.') && name.Trim('.').Length > 0, "Use a relative filename with letters, digits, _, - and . only.");
        if (window)
        {
            Require(input["page"]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)input["page"]), "Specify the exact page.");
            Require(input["locators"] is JArray { Count: >= 1 and <= 500 }, "Supply 1..500 locators from eplan_object_find.");
            foreach (JToken locator in input["locators"]!)
            { GraphicEditPlanner.CheckLocator(locator); }
            Require(input["locators"]!.Select(GraphicEditPlanner.Id).Distinct(StringComparer.Ordinal).Count() == input["locators"]!.Count(), "Remove duplicate locators.");
            Require(input["representation"] is null || input["representation"]!.Type == JTokenType.String, "representation must be a string.");
            string representation = ((string?)input["representation"] ?? "multi_line").ToLowerInvariant();
            Require(representation is "multi_line" or "multiline" or "overview", "representation must be multi_line or Overview.");
            input["representation"] = representation == "overview" ? "Overview" : "MultiLine";
            input["variant"] ??= 0;
            Require(input["variant"]!.Type == JTokenType.Integer && int.TryParse(input["variant"]!.ToString(), out int variant) && variant is >= 0 and <= 15, "variant must be an integer from 0 to 15.");
            Require(input["reference_mm"] is null || input["reference_mm"] is JArray { Count: 2 } point && point.All(v => v.Type is JTokenType.Integer or JTokenType.Float), "reference_mm must contain two finite numbers.");
        }
        else
        {
            Require(input["pages"] is JArray { Count: >= 1 and <= 50 } pages && pages.All(p => p.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)p))
                && pages.Values<string>().Distinct(StringComparer.Ordinal).Count() == pages.Count, "Supply 1..50 distinct exact page names.");
        }
        JToken? description = input["description"];
        Require(description is null || description.Type == JTokenType.String && description.Value<string>() is { Length: <= 500 } || description is JObject texts && texts.Properties().All(p => p.Value.Type == JTokenType.String), "description must be text (at most 500 characters) or a language-to-text object.");
        var violations = new List<string>();
        if (description is JObject translations)
        { PageDescriptionMap.Check(translations.ToObject<Dictionary<string, string>>(), storer.KnowsLanguage, "description", violations); }
        Require(violations.Count == 0, string.Join(" ", violations));
        string path = (string?)input["path"] ?? "";
        Require(Path.IsPathRooted(path) && path.EndsWith(window ? ".ema" : ".emp", StringComparison.OrdinalIgnoreCase), "The Host must supply a full macro output path with the matching extension.");
        if (File.Exists(path) || Directory.Exists(path))
        { throw new BridgeErrorException(ErrorReason.AlreadyExists, "The macro output already exists; choose another name."); }
        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), (string)input["project"]!);
        Func<int[]> write = storer.Prepare(project.Path, input);
        string? before = Snapshot(project.Path, out string? warning);
        JObject identity = new(input);
        foreach (string key in new[] { "path", "dry_run", "fingerprint" })
        { identity.Remove(key); }
        string fingerprint = Fingerprint.Of([before ?? "unavailable", identity.ToString(Formatting.None)]);
        Require(input["fingerprint"] is null || input["fingerprint"]!.Type == JTokenType.String, "fingerprint must be a string.");
        if (input["fingerprint"] is not null && (string?)input["fingerprint"] != fingerprint)
        { throw new BridgeErrorException(ErrorReason.PlanStale, "The project or macro selection changed; preview again."); }
        var result = new JObject
        {
            ["project"] = project.Name,
            ["path"] = path,
            ["fingerprint"] = fingerprint,
            ["changes"] = new JArray(new JObject { ["action"] = "create", ["kind"] = "macro", ["name"] = name })
        };
        if (warning is not null)
        { result["warning"] = warning; }
        if (window)
        { result["variants"] = new JArray((int)input["variant"]!); }
        if ((bool?)input["dry_run"] != false)
        { return result; }
        try
        {
            _ = ExportFolderCheck.CheckEmpty(Path.GetDirectoryName(path));
            int[] variants = write();
            OperationContext.Current.ThrowIfCancellationRequested();
            if (!File.Exists(path) || new FileInfo(path).Length == 0 || window && !variants.Contains((int)input["variant"]!))
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "The stored macro failed file or variant readback."); }
            string? after = Snapshot(project.Path, out warning);
            if (warning is not null)
            { result["warning"] = warning; }
            if (before is not null && after is not null && before != after)
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Macro storage changed the project business snapshot."); }
            if (window)
            { result["variants"] = new JArray(variants); }
            return result;
        }
        catch (Exception error) when (error is not OperationCanceledException && error is not BridgeErrorException { Error.Reason: ErrorReason.Cancelled })
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Macro storage or readback failed; the Host discards the new file.", error); }
    }
    private string? Snapshot(string project, out string? warning)
    {
        warning = null;
        try
        {
            using ISnapshotSource source = snapshots.Open(project);
            return Fingerprint.Of(ApiTryObservations.Capture(source, null, OperationContext.Current.ThrowIfCancellationRequested, _ => null).Select(p => p.Key + "=" + p.Value));
        }
        catch (Exception error) when (error is not OperationCanceledException && error is not BridgeErrorException { Error.Reason: ErrorReason.Cancelled })
        {
            OperationContext.Current.Warn("Project business snapshot is unreadable.", error);
            warning = "Project business snapshot is unreadable; its equality could not be verified.";
            return null;
        }
    }
    private static void Require(bool valid, string message) { if (!valid) { throw WriteArguments.Invalid([message]); } }
}
